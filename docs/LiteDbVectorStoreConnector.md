# LiteDB Vector Store Connector

The LiteDB connector provides an embedded, single-file vector store that implements the `Microsoft.Extensions.VectorData` abstractions. Use it when you need a managed, cross-platform vector store without external dependencies.

## Package

Add a reference to the `Microsoft.SemanticKernel.Connectors.LiteDb` package. The connector depends on `LiteDB` `6.0.0-prerelease.63` to access the new vector index capabilities.

```
dotnet add package Microsoft.SemanticKernel.Connectors.LiteDb
```

## Getting started

````csharp
using Microsoft.SemanticKernel.Connectors.LiteDb;
using Microsoft.Extensions.VectorData;

var store = new LiteDbVectorStore(new LiteDbVectorStoreOptions
{
    ConnectionString = "Filename=./vectors.db;Connection=direct",
});

var collection = store.GetCollection<string, DocumentRecord>("documents");
await collection.EnsureCollectionExistsAsync();

await collection.UpsertAsync(new DocumentRecord
{
    Id = "alpha",
    Title = "LiteDB introduction",
    Vector = new float[] { 0.1f, 0.2f, 0.3f }
});

await foreach (var result in collection.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f }, top: 1))
{
    Console.WriteLine($"Found {result.Record.Title} (score: {result.Score})");
}

public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Title { get; set; } = string.Empty;

    [VectorStoreVector(Dimensions = 3)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
````

### Options

* `ConnectionString` – LiteDB connection string. Required when a database factory is not supplied.
* `CollectionNamePrefix` – optional prefix applied to all collection names created by the store.
* `DefaultDistanceMetric` – default LiteDB distance metric when vector properties do not specify one.
* `AutoCreateVectorIndexes` – automatically ensure vector indexes exist when creating collections.

### Limitations

* Only single string keys are supported in the current implementation.
* Vector properties must use `ReadOnlyMemory<float>` or `float[]`.
* Expression filters are evaluated client side until a translation layer is implemented.
