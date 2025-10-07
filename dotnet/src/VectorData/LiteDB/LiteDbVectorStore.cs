// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.SemanticKernel;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

/// <summary>
/// LiteDB-backed implementation of the <see cref="VectorStore"/> abstraction.
/// </summary>
public sealed class LiteDbVectorStore : VectorStore
{
    private readonly LiteDatabase _database;
    private readonly bool _ownsDatabase;
    private readonly VectorStoreMetadata _metadata;
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly LiteDbVectorStoreOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using a connection string.
    /// </summary>
    /// <param name="connectionString">LiteDB connection string.</param>
    /// <param name="options">Optional store configuration.</param>
    public LiteDbVectorStore(string connectionString, LiteDbVectorStoreOptions? options = null)
        : this(new LiteDatabase(connectionString), ownsDatabase: true, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using an existing <see cref="LiteDatabase"/>.
    /// </summary>
    /// <param name="database">The LiteDB database instance.</param>
    /// <param name="options">Optional store configuration.</param>
    public LiteDbVectorStore(LiteDatabase database, LiteDbVectorStoreOptions? options = null)
        : this(database, ownsDatabase: false, options)
    {
    }

    private LiteDbVectorStore(LiteDatabase database, bool ownsDatabase, LiteDbVectorStoreOptions? options)
    {
        Verify.NotNull(database);

        this._database = database;
        this._ownsDatabase = ownsDatabase;
        this._options = new LiteDbVectorStoreOptions(options);
        this._embeddingGenerator = this._options.EmbeddingGenerator;

        this._metadata = new()
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = "LiteDB"
        };
    }

    /// <inheritdoc />
    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollection() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
#if NET8_0_OR_GREATER
    public override LiteDbCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
    {
        Verify.NotNullOrWhiteSpace(name);

        var options = new LiteDbCollectionOptions
        {
            Definition = definition,
            EmbeddingGenerator = this._embeddingGenerator
        };

        return new LiteDbCollection<TKey, TRecord>(this._database, this.ApplyPrefix(name), options, this._embeddingGenerator);
    }
#if NET8_0_OR_GREATER
    /// <inheritdoc />
    public override LiteDbDynamicCollection GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
    {
        Verify.NotNullOrWhiteSpace(name);
        Verify.NotNull(definition);

        var options = new LiteDbCollectionOptions
        {
            Definition = definition,
            EmbeddingGenerator = this._embeddingGenerator
        };

        return new LiteDbDynamicCollection(this._database, this.ApplyPrefix(name), options, this._embeddingGenerator);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        var definition = new VectorStoreCollectionDefinition
        {
            Properties = new List<VectorStoreProperty>
            {
                new VectorStoreKeyProperty("id", typeof(string))
            }
        };

        var collection = this.GetDynamicCollection(name, definition);
        return collection.CollectionExistsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        var definition = new VectorStoreCollectionDefinition
        {
            Properties = new List<VectorStoreProperty>
            {
                new VectorStoreKeyProperty("id", typeof(string))
            }
        };

        var collection = this.GetDynamicCollection(name, definition);
        return collection.EnsureCollectionDeletedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Verify.NotNull(serviceType);

        return serviceKey is not null ? null : serviceType switch
        {
            var t when t == typeof(VectorStoreMetadata) => this._metadata,
            var t when t == typeof(LiteDatabase) => this._database,
            var t when t.IsInstanceOfType(this) => this,
            _ => null,
        };
    }

    private string ApplyPrefix(string name)
        => string.IsNullOrWhiteSpace(this._options.CollectionNamePrefix)
            ? name
            : string.Concat(this._options.CollectionNamePrefix, name);

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
