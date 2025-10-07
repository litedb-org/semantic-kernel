// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Vector;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class LiteDbCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
#pragma warning restore CA1711
    where TKey : notnull
    where TRecord : class
{
    private readonly LiteDatabase _database;
    private readonly string _collectionName;
    private readonly LiteDbVectorStoreOptions _storeOptions;
    private readonly LiteDbCollectionOptions _collectionOptions;
    private readonly CollectionModel _model;
    private readonly LiteDbRecordMapper<TRecord> _mapper;
    private readonly VectorStoreCollectionMetadata _metadata;

    private static readonly VectorSearchOptions<TRecord> s_defaultSearchOptions = new();

    public LiteDbCollection(LiteDatabase database, string collectionName, LiteDbVectorStoreOptions storeOptions, LiteDbCollectionOptions? collectionOptions = null)
        : this(database, collectionName, storeOptions, collectionOptions, modelFactory: options =>
        {
            if (typeof(TRecord) == typeof(Dictionary<string, object?>))
            {
                throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(LiteDbDynamicCollection)));
            }

            return new LiteDbModelBuilder().Build(typeof(TRecord), options.Definition, options.EmbeddingGenerator);
        })
    {
    }

    internal LiteDbCollection(LiteDatabase database, string collectionName, LiteDbVectorStoreOptions storeOptions, LiteDbCollectionOptions? collectionOptions, Func<LiteDbCollectionOptions, CollectionModel> modelFactory)
    {
        Verify.NotNull(database);
        Verify.NotNullOrWhiteSpace(collectionName);

        this._database = database;
        this._collectionName = collectionName;
        this._storeOptions = storeOptions;
        this._collectionOptions = collectionOptions ?? new LiteDbCollectionOptions();
        this._model = modelFactory(this._collectionOptions);
        this._mapper = new LiteDbRecordMapper<TRecord>(this._model);
        this._metadata = new()
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            CollectionName = collectionName
        };
    }

    public override string Name => this._collectionName;

    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(this._database.CollectionExists(this._collectionName));

    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var collection = this.GetCollection();

        if (this._storeOptions.AutoCreateVectorIndexes)
        {
            foreach (var vectorProperty in this._model.VectorProperties)
            {
                if (vectorProperty.Dimensions <= 0)
                {
                    throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' must specify a dimension when creating LiteDB collections.");
                }

                var metric = LiteDbVectorMath.ToLiteDbMetric(vectorProperty.DistanceFunction, this._storeOptions.DefaultDistanceMetric);
                collection.EnsureIndex(vectorProperty.StorageName, new VectorIndexOptions((ushort)vectorProperty.Dimensions, metric));
            }
        }

        return Task.CompletedTask;
    }

    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        this._database.DropCollection(this._collectionName);
        return Task.CompletedTask;
    }

    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(key);
        options ??= new();

        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var document = this.GetCollection().FindById(new BsonValue(key));
        if (document is null)
        {
            return Task.FromResult<TRecord?>(null);
        }

        var record = this._mapper.ToRecord(document, options.IncludeVectors);
        return Task.FromResult<TRecord?>(record);
    }

    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(key);

        this.GetCollection().Delete(new BsonValue(key));
        return Task.CompletedTask;
    }

    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => this.UpsertAsync([record], cancellationToken);

    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(records);

        var collection = this.GetCollection();

        if (this._database.BeginTrans())
        {
            try
            {
                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = this._mapper.ToDocument(record);
                    this.ValidateVectors(document);
                    collection.Upsert(document);
                }

                this._database.Commit();
            }
            catch
            {
                this._database.Rollback();
                throw;
            }
        }
        else
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = this._mapper.ToDocument(record);
                this.ValidateVectors(document);
                collection.Upsert(document);
            }
        }

        return Task.CompletedTask;
    }

    public override async IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Verify.NotNull(filter);
        Verify.NotLessThan(top, 1);
        options ??= new();

        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        if (options.OrderBy is not null)
        {
            throw new NotSupportedException("LiteDB connector does not currently support ordered filtered retrieval.");
        }

        var compiled = filter.Compile();
        var includeVectors = options.IncludeVectors;
        var skip = options.Skip;
        var returned = 0;

        foreach (var document in this.GetCollection().FindAll())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = this._mapper.ToRecord(document, includeVectors);

            if (!compiled(record))
            {
                continue;
            }

            if (skip > 0)
            {
                skip--;
                continue;
            }

            yield return record;
            returned++;

            if (returned >= top)
            {
                yield break;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(TInput searchValue, int top, VectorSearchOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Verify.NotLessThan(top, 1);
        options ??= s_defaultSearchOptions;

        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = this._model.GetVectorPropertyOrSingle(options);
        var searchVector = this.GetVectorFromInput(searchValue, vectorProperty);
        var includeVectors = options.IncludeVectors;
        var skip = options.Skip;
        var distanceFunction = vectorProperty.DistanceFunction;

        List<(TRecord Record, double Score)> scoredResults;

        if (options.Filter is not null)
        {
            scoredResults = this.SearchWithFilter(options.Filter, searchVector, vectorProperty, includeVectors, cancellationToken);
        }
        else
        {
            scoredResults = this.SearchWithVectorIndex(searchVector, vectorProperty, includeVectors, skip + top, cancellationToken);
        }

        var ordered = LiteDbVectorMath.ShouldSortDescending(distanceFunction)
            ? scoredResults.OrderByDescending(result => result.Score)
            : scoredResults.OrderBy(result => result.Score);

        var enumerated = ordered.Skip(skip).Take(top);

        foreach (var result in enumerated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new VectorSearchResult<TRecord>(result.Record, result.Score);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Verify.NotNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceType == typeof(VectorStoreCollectionMetadata))
        {
            return this._metadata;
        }

        return null;
    }

    protected ILiteCollection<BsonDocument> GetCollection()
        => this._database.GetCollection<BsonDocument>(this._collectionName);

    private void ValidateVectors(BsonDocument document)
    {
        foreach (var vectorProperty in this._model.VectorProperties)
        {
            if (!document.TryGetValue(vectorProperty.StorageName, out var value))
            {
                throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' must be provided.");
            }

            if (!value.IsVector)
            {
                throw new InvalidOperationException($"Field '{vectorProperty.StorageName}' must contain a vector.");
            }

            var vector = (float[])value.RawValue;
            if (vectorProperty.Dimensions > 0 && vector.Length != vectorProperty.Dimensions)
            {
                throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' expected {vectorProperty.Dimensions} dimensions but received {vector.Length}.");
            }
        }
    }

    private List<(TRecord Record, double Score)> SearchWithVectorIndex(ReadOnlyMemory<float> searchVector, VectorPropertyModel vectorProperty, bool includeVectors, int limit, CancellationToken cancellationToken)
    {
        var collection = this.GetCollection();
        var results = new List<(TRecord Record, double Score)>();
        var vectorField = vectorProperty.StorageName;

        foreach (var document in collection.Query().TopKNear(vectorField, searchVector.ToArray(), limit).ToDocuments())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.TryGetValue(vectorField, out var value) || !value.IsVector)
            {
                continue;
            }

            var candidateVector = (float[])value.RawValue;
            var record = this._mapper.ToRecord(document, includeVectors);
            var comparison = LiteDbVectorMath.Compare(searchVector.Span, candidateVector, vectorProperty.DistanceFunction);
            var score = LiteDbVectorMath.ConvertScore(comparison, vectorProperty.DistanceFunction);
            results.Add((record, score));
        }

        return results;
    }

    private List<(TRecord Record, double Score)> SearchWithFilter(Expression<Func<TRecord, bool>> filter, ReadOnlyMemory<float> searchVector, VectorPropertyModel vectorProperty, bool includeVectors, CancellationToken cancellationToken)
    {
        var compiled = filter.Compile();
        var results = new List<(TRecord Record, double Score)>();
        var vectorField = vectorProperty.StorageName;

        foreach (var document in this.GetCollection().FindAll())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!document.TryGetValue(vectorField, out var value) || !value.IsVector)
            {
                continue;
            }

            var record = this._mapper.ToRecord(document, includeVectors);
            if (!compiled(record))
            {
                continue;
            }

            var candidateVector = (float[])value.RawValue;
            var comparison = LiteDbVectorMath.Compare(searchVector.Span, candidateVector, vectorProperty.DistanceFunction);
            var score = LiteDbVectorMath.ConvertScore(comparison, vectorProperty.DistanceFunction);
            results.Add((record, score));
        }

        return results;
    }

    private ReadOnlyMemory<float> GetVectorFromInput<TInput>(TInput searchValue, VectorPropertyModel vectorProperty)
    {
        switch (searchValue)
        {
            case ReadOnlyMemory<float> memory:
                return memory;
            case float[] array:
                return new ReadOnlyMemory<float>(array);
            case IEnumerable<float> enumerable:
                return new ReadOnlyMemory<float>(enumerable.ToArray());
            default:
                throw new NotSupportedException($"Search input type '{typeof(TInput).Name}' is not supported by the LiteDB connector.");
        }
    }
}
