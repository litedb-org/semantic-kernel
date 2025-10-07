// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using LiteDB;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbDynamicCollection : LiteDbCollection<object, Dictionary<string, object?>>
{
    public LiteDbDynamicCollection(LiteDatabase database, string collectionName, LiteDbVectorStoreOptions storeOptions, LiteDbCollectionOptions? collectionOptions)
        : base(database, collectionName, storeOptions, collectionOptions, modelFactory: options =>
        {
            if (options.Definition is null)
            {
                throw new ArgumentException("A record definition must be supplied for dynamic collections.");
            }

            return new LiteDbModelBuilder().BuildDynamic(options.Definition, options.EmbeddingGenerator);
        })
    {
    }
}
