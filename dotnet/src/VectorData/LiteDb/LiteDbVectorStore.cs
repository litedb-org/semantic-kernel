// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Vector store implementation backed by LiteDB.
/// </summary>
public sealed class LiteDbVectorStore : VectorStore
{
    private readonly LiteDatabase _database;
    private readonly bool _ownsDatabase;
    private readonly LiteDbVectorStoreOptions _options;
    private readonly VectorStoreMetadata _metadata;
    private readonly string _connectionIdentifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using the provided connection string.
    /// </summary>
    public LiteDbVectorStore(string connectionString, LiteDbVectorStoreOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        options ??= new LiteDbVectorStoreOptions();
        if (options.Database is not null)
        {
            throw new ArgumentException("When providing a connection string do not supply an existing LiteDatabase instance via options.", nameof(options));
        }

        this._options = options;
        this._database = new LiteDatabase(connectionString);
        this._ownsDatabase = true;
        this._connectionIdentifier = connectionString;
        this._metadata = new VectorStoreMetadata
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = connectionString
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using an existing <see cref="LiteDatabase"/>.
    /// </summary>
    public LiteDbVectorStore(LiteDatabase database, LiteDbVectorStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        options ??= new LiteDbVectorStoreOptions();

        this._options = options;
        this._database = database;
        this._ownsDatabase = options.DisposeDatabase;
        this._connectionIdentifier = LiteDbConstants.VectorStoreSystemName;
        this._metadata = new VectorStoreMetadata
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = LiteDbConstants.VectorStoreSystemName
        };
    }

    /// <inheritdoc />
    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?> call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollection() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?> call GetDynamicCollection() instead.")]
#if NET8_0_OR_GREATER
    public override LiteDbCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
        }
        var collectionOptions = new LiteDbCollectionOptions
        {
            Definition = definition,
            EmbeddingGenerator = this._options.EmbeddingGenerator
        };

        return new LiteDbCollection<TKey, TRecord>(
            this._database,
            name,
            this._options,
            collectionOptions,
            static opts => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(LiteDbDynamicCollection)))
                : new LiteDbModelBuilder().Build(typeof(TRecord), opts.Definition, opts.EmbeddingGenerator),
            this._connectionIdentifier);
    }

#if NET8_0_OR_GREATER
    /// <inheritdoc />
    public override LiteDbCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(definition);

        var collectionOptions = new LiteDbCollectionOptions
        {
            Definition = definition,
            EmbeddingGenerator = this._options.EmbeddingGenerator
        };

        return new LiteDbDynamicCollection(this._database, name, this._options, collectionOptions, this._connectionIdentifier);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var name in this._database.GetCollectionNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return name;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var exists = this._database.GetCollectionNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        this._database.DropCollection(name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is not null
            ? null
            : serviceType == typeof(VectorStoreMetadata)
                ? this._metadata
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && this._ownsDatabase)
        {
            this._database.Dispose();
        }

        base.Dispose(disposing);
    }
}
