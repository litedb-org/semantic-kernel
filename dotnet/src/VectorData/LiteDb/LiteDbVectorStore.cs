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
        : this(NormalizeConnectionOptions(connectionString, options))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using an existing <see cref="LiteDatabase"/>.
    /// </summary>
    public LiteDbVectorStore(LiteDatabase database, LiteDbVectorStoreOptions? options = null)
        : this(NormalizeDatabaseOptions(database, options))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStore"/> class using the supplied options.
    /// </summary>
    public LiteDbVectorStore(LiteDbVectorStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this._options = options;
        var (database, ownsDatabase, connectionIdentifier) = ResolveDatabase(options);

        this._database = database;
        this._ownsDatabase = ownsDatabase;
        this._connectionIdentifier = connectionIdentifier;
        this._metadata = new VectorStoreMetadata
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = connectionIdentifier
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
            EmbeddingGenerator = this._options.EmbeddingGenerator,
            CollectionNamePrefix = this._options.CollectionNamePrefix
        };

        return new LiteDbCollection<TKey, TRecord>(
            this._database,
            this.ResolveCollectionName(name, collectionOptions),
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
            EmbeddingGenerator = this._options.EmbeddingGenerator,
            CollectionNamePrefix = this._options.CollectionNamePrefix
        };

        return new LiteDbDynamicCollection(
            this._database,
            this.ResolveCollectionName(name, collectionOptions),
            this._options,
            collectionOptions,
            this._connectionIdentifier);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var storageName in this._database.GetCollectionNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryNormalizeCollectionName(storageName, out var logicalName))
            {
                continue;
            }

            yield return logicalName;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var storageName = this.ResolveCollectionName(name);
        var exists = this._database.GetCollectionNames().Contains(storageName, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        var storageName = this.ResolveCollectionName(name);
        this._database.DropCollection(storageName);
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

    private static LiteDbVectorStoreOptions NormalizeConnectionOptions(string connectionString, LiteDbVectorStoreOptions? options)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        if (options is not null && (options.Database is not null || options.DatabaseFactory is not null))
        {
            throw new ArgumentException("When providing a connection string do not supply an existing LiteDatabase or database factory via options.", nameof(options));
        }

        var normalized = new LiteDbVectorStoreOptions(options)
        {
            ConnectionString = connectionString
        };

        return normalized;
    }

    private static LiteDbVectorStoreOptions NormalizeDatabaseOptions(LiteDatabase database, LiteDbVectorStoreOptions? options)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (options is not null && options.DatabaseFactory is not null)
        {
            throw new ArgumentException("When providing a LiteDatabase instance do not supply a database factory via options.", nameof(options));
        }

        var normalized = new LiteDbVectorStoreOptions(options)
        {
            Database = database
        };

        return normalized;
    }

    private static (LiteDatabase Database, bool OwnsDatabase, string ConnectionIdentifier) ResolveDatabase(LiteDbVectorStoreOptions options)
    {
        if (options.Database is not null && options.DatabaseFactory is not null)
        {
            throw new ArgumentException("Specify either Database or DatabaseFactory but not both.", nameof(options));
        }

        if (options.Database is not null)
        {
            return (options.Database, options.DisposeDatabase, LiteDbConstants.VectorStoreSystemName);
        }

        if (options.DatabaseFactory is not null)
        {
            var database = options.DatabaseFactory();
            if (database is null)
            {
                throw new InvalidOperationException("The LiteDbVectorStoreOptions.DatabaseFactory returned null.");
            }

            return (database, options.DisposeDatabase, LiteDbConstants.VectorStoreSystemName);
        }

        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? LiteDbConstants.DefaultConnectionString
            : options.ConnectionString;

        var databaseInstance = new LiteDatabase(connectionString);
        return (databaseInstance, options.DisposeDatabase, connectionString);
    }

    private string ResolveCollectionName(string name, LiteDbCollectionOptions? collectionOptions = null)
    {
        var prefix = collectionOptions?.CollectionNamePrefix ?? this._options.CollectionNamePrefix;
        return string.IsNullOrEmpty(prefix) ? name : string.Concat(prefix, name);
    }

    private bool TryNormalizeCollectionName(string storageName, out string logicalName)
    {
        var prefix = this._options.CollectionNamePrefix;
        if (string.IsNullOrEmpty(prefix))
        {
            logicalName = storageName;
            return true;
        }

        if (!storageName.StartsWith(prefix, StringComparison.Ordinal))
        {
            logicalName = string.Empty;
            return false;
        }

        logicalName = storageName[prefix.Length..];
        return true;
    }
}
