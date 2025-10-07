// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Vector;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
/// <summary>
/// Service for storing and retrieving vector records backed by LiteDB.
/// </summary>
/// <typeparam name="TKey">The data type of the record key.</typeparam>
/// <typeparam name="TRecord">The record data model used when interacting with the collection.</typeparam>
public class LiteDbCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<BsonDocument> _collection;
    private readonly CollectionModel _model;
    private readonly LiteDbMapper<TRecord> _mapper;
    private readonly LiteDbFilterTranslator _filterTranslator = new();
    private readonly LiteDbVectorStoreOptions _storeOptions;
    private readonly LiteDbCollectionOptions _options;
    private readonly VectorStoreCollectionMetadata _collectionMetadata;
    private readonly IReadOnlyList<VectorPropertyModel> _vectorProperties;
    private readonly LiteDbDistanceMetric _defaultMetric;

    /// <inheritdoc />
    public override string Name { get; }

    internal LiteDbCollection(
        LiteDatabase database,
        string name,
        LiteDbVectorStoreOptions storeOptions,
        LiteDbCollectionOptions? options,
        Func<LiteDbCollectionOptions, CollectionModel> modelFactory,
        string connectionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(storeOptions);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
        }

        this._database = database;
        this.Name = name;
        this._storeOptions = storeOptions;
        this._options = options ?? LiteDbCollectionOptions.Default;

        this._model = modelFactory(this._options);
        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("LiteDB connector currently supports string keys.");
        }

        this._collection = this._database.GetCollection<BsonDocument>(name);
        this._mapper = new LiteDbMapper<TRecord>(this._model);
        this._vectorProperties = this._model.VectorProperties;
        this._defaultMetric = this._options.DistanceMetric ?? storeOptions.DistanceMetric;

        this._collectionMetadata = new()
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = connectionIdentifier,
            CollectionName = name
        };
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var names = this._database.GetCollectionNames();
        var exists = names.Contains(this.Name, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (this._storeOptions.AutoEnsureVectorIndex && this._vectorProperties.Count > 0)
        {
            foreach (var vectorProperty in this._vectorProperties)
            {
                var dimensions = this._options.VectorDimensions ?? vectorProperty.Dimensions;
                if (dimensions <= 0)
                {
                    throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' must specify dimensions when creating a LiteDB collection.");
                }

                var metric = ResolveMetric(vectorProperty.DistanceFunction, this._options.DistanceMetric ?? this._defaultMetric);
                var path = BsonExpression.Create($"$.{vectorProperty.StorageName}");
                var indexOptions = new VectorIndexOptions((ushort)dimensions, MapMetric(metric));
                this._collection.EnsureIndex(vectorProperty.StorageName, path, indexOptions);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        this._database.DropCollection(this.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        options ??= new RecordRetrievalOptions();

        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var bsonKey = new BsonValue(key);
        var document = this._collection.FindById(bsonKey);
        if (document is null)
        {
            return Task.FromResult<TRecord?>(null);
        }

        var record = this._mapper.MapToRecord(document, options.IncludeVectors);
        return Task.FromResult<TRecord?>(record);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        this._collection.Delete(new BsonValue(key));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => this.UpsertAsync([record], cancellationToken);

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materialized = records as IList<TRecord> ?? records.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, IReadOnlyList<float[]>>? generatedVectors = null;
        if (this._model.EmbeddingGenerationRequired)
        {
            generatedVectors = await this.GenerateEmbeddingsAsync(materialized, cancellationToken).ConfigureAwait(false);
        }

        var documents = new List<BsonDocument>(materialized.Count);
        for (var i = 0; i < materialized.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(this._mapper.MapToDocument(materialized[i], generatedVectors, i));
        }

        this._collection.Upsert(documents);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (top <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top));
        }

        options ??= new FilteredRecordRetrievalOptions<TRecord>();
        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        return this.ExecuteQueryAsync(filter, top, options, cancellationToken);
    }

    private async IAsyncEnumerable<TRecord> ExecuteQueryAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord> options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = this._collection.Query();
        var (expression, parameters) = this._filterTranslator.Translate(filter, this._model);
        query = query.Where(expression, parameters);

        var result = query.Limit(top + options.Skip);
        if (options.Skip > 0)
        {
            result = result.Skip(options.Skip);
        }

        foreach (var document in result.ToEnumerable())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return this._mapper.MapToRecord(document, options.IncludeVectors);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(TInput searchValue, int top, VectorSearchOptions<TRecord>? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (top < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(top));
        }
        options ??= new VectorSearchOptions<TRecord>();

        if (options.IncludeVectors && this._model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = this._model.GetVectorPropertyOrSingle(options);
        var searchVector = await this.ResolveSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        if (searchVector.Length == 0)
        {
            yield break;
        }

        var metric = ResolveMetric(vectorProperty.DistanceFunction, this._options.DistanceMetric ?? this._defaultMetric);
        var query = this._collection.Query();

        if (options.Filter is not null)
        {
            var (expression, parameters) = this._filterTranslator.Translate(options.Filter, this._model);
            query = query.Where(expression, parameters);
        }

        var vectorArray = searchVector.ToArray();
        var take = top + options.Skip;
        var results = query.TopKNear($"$.{vectorProperty.StorageName}", vectorArray, take).ToEnumerable();

        var index = 0;

        foreach (var document in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index++ < options.Skip)
            {
                continue;
            }
            var record = this._mapper.MapToRecord(document, options.IncludeVectors);

            if (!document.TryGetValue(vectorProperty.StorageName, out var value) || value is not BsonVector candidate)
            {
                continue;
            }

            var score = LiteDbVectorMath.Compare(vectorArray, candidate.Values, metric);
            yield return new VectorSearchResult<TRecord>(record, score);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is not null
            ? null
            : serviceType == typeof(VectorStoreCollectionMetadata)
                ? this._collectionMetadata
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<float[]>>?> GenerateEmbeddingsAsync(IList<TRecord> records, CancellationToken cancellationToken)
    {
        if (this._vectorProperties.Count == 0)
        {
            return null;
        }

        Dictionary<string, IReadOnlyList<float[]>>? generated = null;

        foreach (var property in this._vectorProperties)
        {
            var existingValue = property.GetValueAsObject(records[0]);
            if (existingValue is not null && LiteDbMapper<TRecord>.TryConvertVector(existingValue, out _))
            {
                continue;
            }

            if (property.TryGenerateEmbeddings<TRecord, Embedding<float>>(records, cancellationToken, out var task))
            {
                var embeddings = (IReadOnlyList<Embedding<float>>)await task.ConfigureAwait(false);
                generated ??= new Dictionary<string, IReadOnlyList<float[]>>(StringComparer.Ordinal);
                generated[property.ModelName] = embeddings.Select(e => e.Vector.ToArray()).ToList();
            }
            else
            {
                throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TRecord), property.EmbeddingGenerator?.GetType() ?? typeof(object)));
            }
        }

        return generated;
    }

    private async Task<ReadOnlyMemory<float>> ResolveSearchVectorAsync<TInput>(TInput value, VectorPropertyModel property, CancellationToken cancellationToken)
    {
        switch (value)
        {
            case ReadOnlyMemory<float> memory:
                return memory;
            case float[] array:
                return new ReadOnlyMemory<float>(array);
            case Embedding<float> embedding:
                return embedding.Vector;
            default:
                if (property.EmbeddingGenerator is IEmbeddingGenerator<TInput, Embedding<float>> generator)
                {
                    return await generator.GenerateVectorAsync(value, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(value?.GetType() ?? typeof(object), LiteDbModelBuilder.SupportedVectorTypes));
        }
    }

    private static LiteDbDistanceMetric ResolveMetric(string? distanceFunction, LiteDbDistanceMetric fallback)
        => distanceFunction switch
        {
            DistanceFunction.CosineSimilarity => LiteDbDistanceMetric.Cosine,
            DistanceFunction.CosineDistance => LiteDbDistanceMetric.Cosine,
            DistanceFunction.DotProductSimilarity => LiteDbDistanceMetric.DotProduct,
            DistanceFunction.EuclideanDistance => LiteDbDistanceMetric.Euclidean,
            _ => fallback
        };

    private static VectorDistanceMetric MapMetric(LiteDbDistanceMetric metric)
        => metric switch
        {
            LiteDbDistanceMetric.Cosine => VectorDistanceMetric.Cosine,
            LiteDbDistanceMetric.DotProduct => VectorDistanceMetric.DotProduct,
            LiteDbDistanceMetric.Euclidean => VectorDistanceMetric.Euclidean,
            _ => throw new NotSupportedException($"Unsupported distance metric '{metric}'.")
        };
}
