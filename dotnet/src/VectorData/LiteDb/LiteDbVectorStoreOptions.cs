// Copyright (c) Microsoft. All rights reserved.

using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Options when creating a <see cref="LiteDbVectorStore"/>.
/// </summary>
public sealed class LiteDbVectorStoreOptions
{
    internal static readonly LiteDbVectorStoreOptions Default = new();

    /// <summary>
    /// Gets or sets the connection string used to open the LiteDB database.
    /// </summary>
    /// <remarks>
    /// When not provided a shared-file connection string is derived from the collection options.
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
    public bool AutoEnsureVectorIndex { get; set; } = true;

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating embeddings for vector properties.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
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
