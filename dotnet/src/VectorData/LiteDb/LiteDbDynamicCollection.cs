// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using LiteDB;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

#pragma warning disable CA1711
/// <summary>
/// Represents a LiteDB collection mapped to dynamic <see cref="Dictionary{TKey, TValue}"/> records.
/// </summary>
public sealed class LiteDbDynamicCollection : LiteDbCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiteDbDynamicCollection"/> class.
    /// </summary>
    public LiteDbDynamicCollection(
        LiteDatabase database,
        string name,
        LiteDbVectorStoreOptions storeOptions,
        LiteDbCollectionOptions options,
        string connectionIdentifier)
        : base(
            database,
            name,
            storeOptions,
            options,
            static opts => new LiteDbModelBuilder()
                .BuildDynamic(opts.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"), opts.EmbeddingGenerator),
            connectionIdentifier)
    {
    }
}
