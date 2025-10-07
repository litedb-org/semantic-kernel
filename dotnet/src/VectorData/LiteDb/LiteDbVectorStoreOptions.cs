// Copyright (c) Microsoft. All rights reserved.

using System;
using LiteDB;
using Microsoft.Extensions.AI;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Options when creating a <see cref="LiteDbVectorStore"/>.
/// </summary>
public sealed class LiteDbVectorStoreOptions
{
    internal static readonly LiteDbVectorStoreOptions Default = new();
    private bool _autoEnsureVectorIndex = true;
    private bool? _autoCreateVectorIndexes;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStoreOptions"/> class.
    /// </summary>
    public LiteDbVectorStoreOptions()
    {
    }

    internal LiteDbVectorStoreOptions(LiteDbVectorStoreOptions? source)
    {
        if (source is null)
        {
            return;
        }

        this.ConnectionString = source.ConnectionString;
        this.Database = source.Database;
        this.DatabaseFactory = source.DatabaseFactory;
        this.DisposeDatabase = source.DisposeDatabase;
        this.DistanceMetric = source.DistanceMetric;
        this.AutoEnsureVectorIndex = source.AutoEnsureVectorIndex;
        this.AutoCreateVectorIndexes = source.AutoCreateVectorIndexes;
        this.EmbeddingGenerator = source.EmbeddingGenerator;
        this.CollectionNamePrefix = source.CollectionNamePrefix;
    }

    /// <summary>
    /// Gets or sets the connection string used to open the LiteDB database.
    /// </summary>
    /// <remarks>
    /// When not provided, a shared-file connection string is derived automatically.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="LiteDatabase"/> instance used by the store.
    /// </summary>
    /// <remarks>
    /// When provided, the caller retains ownership unless <see cref="DisposeDatabase"/> is set to <see langword="true"/>.
    /// </remarks>
    public LiteDatabase? Database { get; set; }

    /// <summary>
    /// Gets or sets the factory used to create a <see cref="LiteDatabase"/> instance when constructing the store.
    /// </summary>
    /// <remarks>
    /// When provided, <see cref="Database"/> and <see cref="ConnectionString"/> are ignored.
    /// </remarks>
    public Func<LiteDatabase>? DatabaseFactory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the store should dispose the supplied database when disposed.
    /// </summary>
    public bool DisposeDatabase { get; set; } = true;

    /// <summary>
    /// Gets or sets the default distance metric to use when building vector indexes.
    /// </summary>
    public LiteDbDistanceMetric DistanceMetric { get; set; } = LiteDbDistanceMetric.Cosine;

    /// <summary>
    /// Gets or sets a value indicating whether collections created through this store automatically ensure their vector index.
    /// </summary>
    public bool AutoEnsureVectorIndex
    {
        get => this._autoEnsureVectorIndex;
        set
        {
            this._autoEnsureVectorIndex = value;
            this._autoCreateVectorIndexes = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether collections created through this store automatically ensure their vector index.
    /// </summary>
    public bool? AutoCreateVectorIndexes
    {
        get => this._autoCreateVectorIndexes;
        set
        {
            this._autoCreateVectorIndexes = value;
            if (value.HasValue)
            {
                this._autoEnsureVectorIndex = value.Value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating embeddings for vector properties.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>
    /// Gets or sets the prefix applied to collection names created through this store.
    /// </summary>
    public string? CollectionNamePrefix { get; set; }
}

/// <summary>
/// LiteDB distance metrics supported by the connector.
/// </summary>
public enum LiteDbDistanceMetric
{
    /// <summary>Cosine similarity.</summary>
    Cosine,

    /// <summary>L2 (Euclidean) distance.</summary>
    Euclidean,

    /// <summary>Dot product similarity.</summary>
    DotProduct,
}
