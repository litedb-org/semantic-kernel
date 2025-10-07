// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using LiteDB;
using Microsoft.Extensions.AI;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
/// <summary>
/// LiteDB-backed dynamic vector collection that stores flexible schema records.
/// </summary>
public sealed class LiteDbDynamicCollection : LiteDbCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbDynamicCollection"/> class.
    /// </summary>
    /// <param name="database">The <see cref="LiteDatabase"/> hosting the collection.</param>
    /// <param name="name">Collection name.</param>
    /// <param name="options">Dynamic collection configuration options.</param>
    /// <param name="defaultEmbeddingGenerator">Fallback embedding generator.</param>
    public LiteDbDynamicCollection(LiteDatabase database, string name, LiteDbCollectionOptions options, IEmbeddingGenerator? defaultEmbeddingGenerator)
        : base(
            database,
            name,
            opts => new LiteDbModelBuilder().BuildDynamic(
                opts.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                opts.EmbeddingGenerator ?? defaultEmbeddingGenerator),
            options,
            defaultEmbeddingGenerator)
    {
    }
}
