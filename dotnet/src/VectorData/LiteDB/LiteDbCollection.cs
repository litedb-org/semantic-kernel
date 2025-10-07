// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Vector;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.SemanticKernel;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
/// <summary>
/// LiteDB-backed implementation of a vector store collection.
/// </summary>
/// <typeparam name="TKey">Type used for the record key. LiteDB currently supports string keys.</typeparam>
/// <typeparam name="TRecord">The record type mapped to stored BSON documents.</typeparam>
public class LiteDbCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
#pragma warning restore CA1711
    where TKey : notnull
    where TRecord : class
{
    private readonly LiteDatabase _database;
    private readonly string _collectionName;
    private readonly LiteDbCollectionOptions _options;
    private readonly CollectionModel _model;
    private readonly LiteDbMapper<TRecord> _mapper;
    private readonly LiteDbFilterTranslator _filterTranslator;
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly VectorStoreCollectionMetadata _metadata;

    private ILiteCollection<BsonDocument> Collection => this._database.GetCollection<BsonDocument>(this._collectionName);

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="database">The <see cref="LiteDatabase"/> instance backing the collection.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="options">Optional collection configuration.</param>
    /// <param name="defaultEmbeddingGenerator">Optional embedding generator applied when records do not specify one.</param>
    public LiteDbCollection(LiteDatabase database, string name, LiteDbCollectionOptions? options = null, IEmbeddingGenerator? defaultEmbeddingGenerator = null)
        : this(
            database,
            name,
            opts => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(LiteDbDynamicCollection)))
                : new LiteDbModelBuilder().Build(typeof(TRecord), opts.Definition, opts.EmbeddingGenerator ?? defaultEmbeddingGenerator),
            options,
            defaultEmbeddingGenerator)
    {
    }

    internal LiteDbCollection(
        LiteDatabase database,
        string name,
        Func<LiteDbCollectionOptions, CollectionModel> modelFactory,
        LiteDbCollectionOptions? options,
        IEmbeddingGenerator? defaultEmbeddingGenerator)
    {
        Verify.NotNull(database);
        Verify.NotNullOrWhiteSpace(name);

        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("LiteDB vector collections currently support string keys only.");
        }

        this._database = database;
        this._collectionName = name;

        options ??= LiteDbCollectionOptions.Default;
        this._options = new LiteDbCollectionOptions(options);

        if (this._options.EmbeddingGenerator is null && defaultEmbeddingGenerator is not null)
        {
            this._options.EmbeddingGenerator = defaultEmbeddingGenerator;
        }

        this._embeddingGenerator = this._options.EmbeddingGenerator ?? defaultEmbeddingGenerator;
        this._model = modelFactory(this._options);
        this._mapper = new LiteDbMapper<TRecord>(this._model);
        this._filterTranslator = new LiteDbFilterTranslator();

        this._metadata = new()
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = "LiteDB",
            CollectionName = name,
        };
    }

    /// <inheritdoc />
    public override string Name => this._collectionName;

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this._database.GetCollectionNames().Contains(this._collectionName));
    }

    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collection = this.Collection;
        if (this._options.AutoCreateVectorIndex && this._model.VectorProperties.Count > 0)
        {
            var vectorProperty = this._model.VectorProperty;
            var metric = LiteDbDistanceMetric.ToLiteDbMetric(vectorProperty.DistanceFunction ?? this._options.DistanceFunction);
            var options = new VectorIndexOptions((ushort)(vectorProperty.Dimensions > 0 ? vectorProperty.Dimensions : this._options.Dimensions), metric);
            collection.EnsureIndex(vectorProperty.StorageName, doc => doc[vectorProperty.StorageName], options);
        }

        foreach (var dataProperty in this._model.DataProperties)
        {
            if (dataProperty.IsIndexed)
            {
                collection.EnsureIndex(dataProperty.StorageName);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this._database.DropCollection(this._collectionName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var record = this.Collection.FindById(new BsonValue(key.ToString()));
        if (record is null)
        {
            return Task.FromResult<TRecord?>(null);
        }

        var includeVectors = options?.IncludeVectors ?? false;
        var mapped = this._mapper.MapFromStorageToDataModel(record, includeVectors);
        return Task.FromResult<TRecord?>(mapped);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        this.Collection.Delete(new BsonValue(key.ToString()));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => this.UpsertAsync(new[] { record }, cancellationToken);

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        var materialized = records as IList<TRecord> ?? records.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        IReadOnlyList<Embedding>?[]? generatedEmbeddings = null;
        var vectorPropertyCount = this._model.VectorProperties.Count;
        for (var i = 0; i < vectorPropertyCount; i++)
        {
            var vectorProperty = this._model.VectorProperties[i];
            if (LiteDbModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
            {
                continue;
            }

            if (!vectorProperty.TryGenerateEmbeddings<TRecord, Embedding<float>>(materialized, cancellationToken, out var task))
            {
                throw new InvalidOperationException($"The embedding generator configured on property '{vectorProperty.ModelName}' cannot produce embeddings for the provided record type.");
            }

            generatedEmbeddings ??= new IReadOnlyList<Embedding>?[vectorPropertyCount];
            generatedEmbeddings[i] = (IReadOnlyList<Embedding<float>>)await task.ConfigureAwait(false);
        }

        var collection = this.Collection;
        var index = 0;
        foreach (var record in materialized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = this._mapper.MapFromDataToStorageModel(record, index, generatedEmbeddings);
            collection.Upsert(document);
            index++;
        }
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default)
    {
        Verify.NotNull(filter);
        Verify.NotLessThan(top, 1);

        return this.ExecuteFilteredQuery(filter, top, options?.IncludeVectors ?? false, cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(TInput searchValue, int top, VectorSearchOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Verify.NotNull(searchValue);
        Verify.NotLessThan(top, 1);

        options ??= new();

        var vectorProperty = this._model.GetVectorPropertyOrSingle(options);
        var queryVector = await this.ResolveSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        var collection = this.Collection;
        var filterExpression = options.Filter is null ? null : this._filterTranslator.Translate(options.Filter, this._model);
        var liteQuery = collection.Query();
        if (filterExpression is not null)
        {
            liteQuery = liteQuery.Where(filterExpression);
        }

        var liteResults = liteQuery.TopKNear(vectorProperty.StorageName, queryVector, top + options.Skip);
        var includeVectors = options.IncludeVectors;
        var skip = options.Skip;
        var taken = 0;

        foreach (var document in liteResults.ToEnumerable())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (skip > 0)
            {
                skip--;
                continue;
            }

            var candidateVector = (document[vectorProperty.StorageName] as BsonVector)?.Values ?? Array.Empty<float>();
            var similarity = LiteDbDistanceMetric.ComputeSimilarity(queryVector.AsSpan(), candidateVector, vectorProperty.DistanceFunction);

            var mapped = this._mapper.MapFromStorageToDataModel(document, includeVectors);
            yield return new VectorSearchResult<TRecord>(mapped, similarity);

            taken++;
            if (taken >= top)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Verify.NotNull(serviceType);

        return serviceKey is not null ? null : serviceType switch
        {
            var t when t == typeof(VectorStoreCollectionMetadata) => this._metadata,
            var t when t.IsInstanceOfType(this) => this,
            _ => null,
        };
    }

    private async IAsyncEnumerable<TRecord> ExecuteFilteredQuery(Expression<Func<TRecord, bool>> filter, int top, bool includeVectors, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var expression = this._filterTranslator.Translate(filter, this._model);
        var query = this.Collection.Query();
        if (expression is not null)
        {
            query = query.Where(expression);
        }

        var results = query.Limit(top).ToEnumerable();
        foreach (var document in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return this._mapper.MapFromStorageToDataModel(document, includeVectors);
        }
    }

    private async Task<float[]> ResolveSearchVectorAsync<TInput>(TInput searchValue, VectorPropertyModel vectorProperty, CancellationToken cancellationToken)
        where TInput : notnull
    {
        if (searchValue is float[] directArray)
        {
            return directArray;
        }

        ReadOnlyMemory<float> memory = searchValue switch
        {
            ReadOnlyMemory<float> rom => rom,
            Embedding<float> embedding => embedding.Vector,
            _ when vectorProperty.EmbeddingGenerator is IEmbeddingGenerator<TInput, Embedding<float>> generator
                => await generator.GenerateVectorAsync(searchValue, cancellationToken: cancellationToken).ConfigureAwait(false),
            _ when this._embeddingGenerator is IEmbeddingGenerator<TInput, Embedding<float>> sharedGenerator
                => await sharedGenerator.GenerateVectorAsync(searchValue, cancellationToken: cancellationToken).ConfigureAwait(false),
            _ when vectorProperty.EmbeddingGenerator is null && this._embeddingGenerator is null
                => throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), LiteDbModelBuilder.SupportedVectorTypes)),
            _ => throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), (vectorProperty.EmbeddingGenerator ?? this._embeddingGenerator)!.GetType()))
        };

        if (memory.Length == 0)
        {
            throw new InvalidOperationException("Search vector cannot be empty.");
        }

        if (MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null && segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return memory.ToArray();
    }
}
