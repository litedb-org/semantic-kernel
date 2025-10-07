// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

/// <summary>
/// Options for configuring a <see cref="LiteDbVectorStore"/>.
/// </summary>
public sealed class LiteDbVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbVectorStoreOptions"/> class.
    /// </summary>
    public LiteDbVectorStoreOptions()
    {
    }

    internal LiteDbVectorStoreOptions(LiteDbVectorStoreOptions? other)
    {
        this.EmbeddingGenerator = other?.EmbeddingGenerator;
        this.CollectionNamePrefix = other?.CollectionNamePrefix;
    }

    /// <summary>
    /// Gets or sets the default embedding generator for collections created from this store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix applied to LiteDB collection names.
    /// </summary>
    public string? CollectionNamePrefix { get; set; }
}
