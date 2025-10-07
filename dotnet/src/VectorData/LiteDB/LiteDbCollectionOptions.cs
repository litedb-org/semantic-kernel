// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

/// <summary>
/// Options for configuring a LiteDB-backed vector collection.
/// </summary>
public sealed class LiteDbCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly LiteDbCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbCollectionOptions"/> class.
    /// </summary>
    public LiteDbCollectionOptions()
    {
    }

    internal LiteDbCollectionOptions(LiteDbCollectionOptions? other)
        : base(other)
    {
        this.Dimensions = other?.Dimensions ?? Default.Dimensions;
        this.DistanceFunction = other?.DistanceFunction ?? Default.DistanceFunction;
        this.AutoCreateVectorIndex = other?.AutoCreateVectorIndex ?? Default.AutoCreateVectorIndex;
    }

    /// <summary>
    /// Gets or sets the expected dimensionality of vectors stored in the collection.
    /// </summary>
    public int Dimensions { get; set; }

    /// <summary>
    /// Gets or sets the distance function used when creating the LiteDB vector index.
    /// </summary>
    public string DistanceFunction { get; set; } = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity;

    /// <summary>
    /// Gets or sets a value indicating whether the connector should automatically create the LiteDB vector index.
    /// </summary>
    public bool AutoCreateVectorIndex { get; set; } = true;
}
