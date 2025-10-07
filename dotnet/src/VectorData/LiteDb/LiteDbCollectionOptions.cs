// Copyright (c) Microsoft. All rights reserved.

using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Options for configuring a LiteDB vector collection.
/// </summary>
public sealed class LiteDbCollectionOptions
{
    internal static readonly LiteDbCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbCollectionOptions"/> class.
    /// </summary>
    public LiteDbCollectionOptions()
    {
    }

    internal LiteDbCollectionOptions(LiteDbCollectionOptions? source)
    {
        if (source is null)
        {
            return;
        }

        this.Definition = source.Definition;
        this.EmbeddingGenerator = source.EmbeddingGenerator;
        this.VectorDimensions = source.VectorDimensions;
        this.DistanceMetric = source.DistanceMetric;
        this.ConnectionString = source.ConnectionString;
        this.CollectionNamePrefix = source.CollectionNamePrefix;
    }

    /// <summary>
    /// Gets or sets the schema definition for dynamic collections.
    /// </summary>
    public VectorStoreCollectionDefinition? Definition { get; set; }

    /// <summary>
    /// Gets or sets the embedding generator used to populate vector properties for this collection.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>
    /// Gets or sets the dimensionality of the stored vectors.
    /// </summary>
    public int? VectorDimensions { get; set; }

    /// <summary>
    /// Gets or sets the distance metric to use for the vector index.
    /// </summary>
    public LiteDbDistanceMetric? DistanceMetric { get; set; }

    /// <summary>
    /// Gets or sets the LiteDB connection string override.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the collection name prefix applied when creating tables.
    /// </summary>
    public string? CollectionNamePrefix { get; set; }
}
