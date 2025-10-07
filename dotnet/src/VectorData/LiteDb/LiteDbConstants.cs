// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal static class LiteDbConstants
{
    internal const string VectorStoreSystemName = "LiteDB";
    internal const string DefaultConnectionString = "Filename=LiteDbVectorStore.db;Connection=shared";
    internal const string DefaultKeyField = "_id";
}
