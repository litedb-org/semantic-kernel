// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

/// <summary>
/// Options used when creating a <see cref="LiteDbCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class LiteDbCollectionOptions
{
    /// <summary>
    /// Gets or sets the optional collection definition describing how the record type maps to LiteDB.
    /// </summary>
    public VectorStoreCollectionDefinition? Definition { get; set; }

    /// <summary>
    /// Gets or sets the embedding generator to use for vector properties when the record definition relies on generated vectors.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
