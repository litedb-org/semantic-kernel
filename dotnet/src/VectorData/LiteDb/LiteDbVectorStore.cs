// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Vector;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// LiteDB backed implementation of <see cref="VectorStore"/>.
/// </summary>
public sealed class LiteDbVectorStore : VectorStore
{
    private readonly LiteDatabase _database = null!;
    private readonly LiteDbVectorStoreOptions _options;
    private readonly bool _ownsDatabase;

    private readonly VectorStoreMetadata _metadata;

    public LiteDbVectorStore(LiteDbVectorStoreOptions? options = null)
    {
        this._options = options ?? new LiteDbVectorStoreOptions();
        (this._database, this._ownsDatabase) = CreateDatabase(this._options);

        this._metadata = new()
        {
            VectorStoreSystemName = LiteDbConstants.VectorStoreSystemName,
            VectorStoreName = this._options.ConnectionString
        };
    }

    internal LiteDatabase Database => this._database;

    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollection() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
    {
        Verify.NotNullOrWhiteSpace(name);

        if (typeof(TKey) != typeof(string))
        {
            throw new NotSupportedException("LiteDB vector store collections require string keys.");
        }

        var options = new LiteDbCollectionOptions
        {
            Definition = definition
        };

        return new LiteDbCollection<TKey, TRecord>(
            this._database,
            this.GetPhysicalCollectionName(name),
            this._options,
            options);
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
    {
        Verify.NotNullOrWhiteSpace(name);
        Verify.NotNull(definition);

        var options = new LiteDbCollectionOptions
        {
            Definition = definition
        };

        return new LiteDbDynamicCollection(
            this._database,
            this.GetPhysicalCollectionName(name),
            this._options,
            options);
    }

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var collection in this._database.GetCollectionNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(this._options.CollectionNamePrefix) && collection.StartsWith(this._options.CollectionNamePrefix, StringComparison.Ordinal))
            {
                yield return collection[this._options.CollectionNamePrefix.Length..];
            }
            else if (string.IsNullOrEmpty(this._options.CollectionNamePrefix))
            {
                yield return collection;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(name);

        var physicalName = this.GetPhysicalCollectionName(name);
        return Task.FromResult(this._database.CollectionExists(physicalName));
    }

    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(name);

        var physicalName = this.GetPhysicalCollectionName(name);
        this._database.DropCollection(physicalName);
        return Task.CompletedTask;
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

        if (serviceType == typeof(VectorStoreMetadata))
        {
            return this._metadata;
        }

        if (serviceType == typeof(LiteDatabase))
        {
            return this._database;
        }

        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && this._ownsDatabase)
        {
            this._database.Dispose();
        }

        base.Dispose(disposing);
    }

    private static (LiteDatabase database, bool ownsDatabase) CreateDatabase(LiteDbVectorStoreOptions options)
    {
        if (options.DatabaseFactory is not null)
        {
            return (options.DatabaseFactory(), options.DisposeDatabase);
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("A connection string or database factory must be provided.", nameof(options));
        }

        return (new LiteDatabase(options.ConnectionString), true);
    }

    private string GetPhysicalCollectionName(string logicalName)
    {
        if (string.IsNullOrEmpty(this._options.CollectionNamePrefix))
        {
            return logicalName;
        }

        return this._options.CollectionNamePrefix + logicalName;
    }
}
