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

```csharp
var options = new LiteDbVectorStoreOptions
{
    DatabaseFactory = () => new LiteDatabase("Filename=sk.db;Connection=shared"),
    CollectionNamePrefix = "sk_"
};

using var store = new LiteDbVectorStore(options);
```

Collections created through this store are materialized as `sk_*` tables, while APIs such as `CollectionExistsAsync` and `ListCollectionNamesAsync` use the logical names you pass to `GetCollection`.

`LiteDbVectorStoreOptions` honor the precedence `DatabaseFactory` → `Database` → `ConnectionString`. When `AutoCreateVectorIndexes` (or the legacy `AutoEnsureVectorIndex`) is set to `false`, collections skip index provisioning and you can manage indexes manually.

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

### Filter translation

LiteDB evaluates filters server-side. The table below shows how common Semantic Kernel expressions translate into LiteDB predicates:

| Semantic Kernel expression | LiteDB predicate |
| --------------------------- | ---------------- |
| `hotel => hotel.Rating >= 4` | `($.Rating >= @0)` |
| `hotel => hotel.City.StartsWith("Sea")` | `($.City LIKE @0)` with `@0 = "Sea%"` |
| `hotel => hotel.City.EndsWith("town")` | `($.City LIKE @0)` with `@0 = "%town"` |
| `hotel => hotel.City.Contains("port")` | `($.City LIKE @0)` with `@0 = "%port%"` |
| `hotel => hotel.Tags.Contains("spa")` | `(@0 IN $.Tags)` |
| `allowedCities.Contains(hotel.City!)` | `($.City IN @0)` |
| `hotel => hotel.City == null` | `($.City = null)` |
| `hotel => hotel.City != null` | `($.City != null)` |

Captured variables are parameterized (`@0`, `@1`, …) so repeated values are not interpolated into the query text.

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

The store-level generator is inherited by collections, and you can override it per property through a `VectorStoreCollectionDefinition`. This allows different embedding generators to service different vector fields within the same record.

## Transactions and batch ingestion

`UpsertAsync(IEnumerable<TRecord>)` wraps batched writes in a LiteDB transaction. Either every document is inserted or updated, or none of them are. Single-record upserts remain non-transactional for minimum overhead.

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

## Performance notes

- Vector index creation happens on demand during `EnsureCollectionExistsAsync`. For large collections it is faster to ingest in batches and call `EnsureCollectionExistsAsync` once.
- Server-side filters are most effective when the predicate is selective (for example, indexed equality or range comparisons) before vector search runs.
- LiteDB's vector APIs are synchronous; high-throughput scenarios should run on background threads or batch operations.

## Limitations

- LiteDB databases are single-process; avoid opening the same file from multiple processes simultaneously. (although supported)
- `IncludeVectors` cannot be enabled on retrieval operations when embedding generation is configured, matching the behavior of other Semantic Kernel connectors.
