# LiteDB Vector Store Connector

The LiteDB connector provides an embedded, single-file option for working with the Semantic Kernel vector store abstractions. It uses LiteDB v6's native `BsonVector` storage and HNSW indexes so that applications can run entirely on managed runtimes without external dependencies.

## Prerequisites

- Install the `LiteDB` NuGet package version `6.0.0-prerelease.63`.
- Reference the `Microsoft.SemanticKernel.Connectors.LiteDb` project or package in your .NET solution.
- Ensure the target framework is at least .NET Standard 2.1 or .NET 8.0.

## Creating a vector store

```csharp
using LiteDB;
using Microsoft.SemanticKernel.Connectors.LiteDb;

using var database = new LiteDatabase("Filename=sk.db;Mode=Exclusive");
var store = new LiteDbVectorStore(database);
```

The store can also open a LiteDB database using a connection string. When you pass an existing `LiteDatabase` instance you control its lifecycle via `LiteDbVectorStoreOptions.DisposeDatabase`.

When you need the connector to manage the database lifecycle, create the store with `LiteDbVectorStoreOptions`. The options surface supports supplying a database factory and a collection name prefix.

Connection sources are considered in the following order: a configured `DatabaseFactory` is used first, then an existing `LiteDatabase` instance via `Database`, and finally the `ConnectionString` (or the embedded default when none is supplied). The `AutoCreateVectorIndexes` property is an alias for `AutoEnsureVectorIndex` so that existing configuration snippets continue to work unchanged.

```csharp
var options = new LiteDbVectorStoreOptions
{
    DatabaseFactory = () => new LiteDatabase("Filename=sk.db;Connection=shared"),
    CollectionNamePrefix = "sk_"
};

using var store = new LiteDbVectorStore(options);
```

Collections created through this store are materialized as `sk_*` tables, while APIs such as `CollectionExistsAsync` and `ListCollectionNamesAsync` use the logical names you pass to `GetCollection`.

## Defining a collection

Collections map strongly-typed record models to LiteDB BSON documents. Use the Semantic Kernel attributes to annotate key, data, and vector fields:

```csharp
public sealed class Hotel
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string Name { get; set; } = string.Empty;

    [VectorStoreVector(Dimensions: 3, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float>? DescriptionEmbedding { get; set; }
}

var collection = store.GetCollection<string, Hotel>("hotels");
await collection.EnsureCollectionExistsAsync();
```

The connector automatically provisions HNSW vector indexes when `EnsureCollectionExistsAsync` is called and a vector property is present. Distance metrics default to cosine similarity but can be overridden through attributes or `LiteDbCollectionOptions`.

## Filtering

LiteDB filter translation covers the standard comparison operators along with string and set membership helpers. The following table shows a subset of the mappings:

| Semantic Kernel filter | LiteDB predicate |
| --- | --- |
| `r => r.Rating >= 4` | `($.Rating >= @0)` |
| `r => r.City.StartsWith("Sea")` | `($.City LIKE @0)` |
| `r => r.Description.EndsWith("Inn")` | `($.Description LIKE @0)` |
| `r => r.Tags.Contains("spa")` | `($.Tags ANY = @0)` |
| `r => new[] { "Seattle", "Portland" }.Contains(r.City)` | `($.City IN @0)` |

Captured variables are parameterized, and unsupported constructs (such as the `StringComparison` overloads) throw `NotSupportedException` so that filters never silently fall back to client-side evaluation.

## Generating embeddings on the fly

When a record's vector property is a non-vector type (for example `string` or `DataContent`), configure an `IEmbeddingGenerator` so that LiteDB receives vectors during `UpsertAsync` and vector search:

```csharp
var options = new LiteDbVectorStoreOptions
{
    EmbeddingGenerator = myTextEmbeddingGenerator
};

var store = new LiteDbVectorStore("Filename=sk.db", options);
var collection = store.GetCollection<string, Article>("articles");
```

The store-level generator is inherited by collections, but you can override it per collection via `LiteDbCollectionOptions` or per property through a `VectorStoreCollectionDefinition`.

## Batch upserts and transactions

`UpsertAsync(IEnumerable<TRecord>)` executes inside a LiteDB transaction. Either all documents in the batch are written or an exception is thrown and the collection is left unchanged. Single-record upserts skip the transaction for better throughput.

## Dependency injection

`Microsoft.Extensions.DependencyInjection` helpers are available so that the LiteDB connector participates in common hosting patterns:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Connectors.LiteDb;

var services = new ServiceCollection();

services.AddLiteDbVectorStore(sp => new LiteDbVectorStoreOptions
{
    DatabaseFactory = () => new LiteDatabase("Filename=sk.db;Connection=shared")
});

services.AddLiteDbCollection<string, Hotel>("hotels");
services.AddLiteDbDynamicCollection("snippets", _ => definition);
```

Registered collections automatically resolve a `VectorStoreCollection<TKey, TRecord>` and `IVectorSearchable<TRecord>` backed by the keyed store, and they inherit the embedding generator registered in the DI container when one is not provided in the options.

## Performance considerations

- Index creation happens when `EnsureCollectionExistsAsync` runs. For large collections, schedule this ahead of ingesting data or during maintenance windows.
- Filters that leverage string operators and `IN` clauses are translated to server-side predicates; prefer them over manual in-memory filtering for better selectivity.
- Vectors are validated at write time so that accidental dimension mismatches are caught before they reach storage.

## Dynamic collections

Dynamic collections let you work with `Dictionary<string, object?>` payloads without defining a CLR type:

```csharp
var definition = new VectorStoreCollectionDefinition
{
    Properties =
    {
        new VectorStoreKeyProperty("Id", typeof(string)),
        new VectorStoreDataProperty("Category", typeof(string)),
        new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), dimensions: 3)
    }
};

var dynamicCollection = store.GetDynamicCollection("snippets", definition);
```

## Limitations

- LiteDB's vector APIs are synchronous; high-throughput scenarios should run on background threads or batch operations.
- LiteDB databases are single-process; avoid opening the same file from multiple processes simultaneously. (although supported)
- `IncludeVectors` cannot be enabled on retrieval operations when embedding generation is configured, matching the behavior of other Semantic Kernel connectors.
