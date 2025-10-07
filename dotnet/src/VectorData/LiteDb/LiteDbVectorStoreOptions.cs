// Copyright (c) Microsoft. All rights reserved.

using System;
using LiteDB;
using LiteDB.Vector;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Options used to configure <see cref="LiteDbVectorStore"/>.
/// </summary>
public sealed class LiteDbVectorStoreOptions
{
    /// <summary>
    /// Gets or sets the LiteDB connection string used to create the underlying <see cref="LiteDatabase"/>.
    /// </summary>
    /// <remarks>
    /// The connection string is required when <see cref="DatabaseFactory"/> is not provided.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the factory used to create the underlying <see cref="LiteDatabase"/> instance.
    /// </summary>
    /// <remarks>
    /// When specified, <see cref="ConnectionString"/> is ignored. The caller is responsible for configuring the
    /// <see cref="LiteDatabase"/> with any desired options such as shared connections or logging.
    /// </remarks>
    public Func<LiteDatabase>? DatabaseFactory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the <see cref="LiteDatabase"/> instance should be disposed when the
    /// <see cref="LiteDbVectorStore"/> is disposed.
    /// </summary>
    public bool DisposeDatabase { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional prefix applied to every LiteDB collection name created by the vector store.
    /// </summary>
    /// <remarks>
    /// This can be used to avoid collisions when multiple vector stores share the same database file.
    /// </remarks>
    public string CollectionNamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default distance metric used when creating vector indexes.
    /// </summary>
    public VectorDistanceMetric DefaultDistanceMetric { get; set; } = VectorDistanceMetric.Cosine;

    /// <summary>
    /// Gets or sets a value indicating whether vector indexes should be automatically created when a collection is ensured.
    /// </summary>
    public bool AutoCreateVectorIndexes { get; set; } = true;
}
