# LiteDB Vector Store Connector

The LiteDB connector provides an embedded, single-file vector store implementation for Semantic Kernel applications.

## Installation

Add a package reference to `Microsoft.SemanticKernel.Connectors.LiteDB` and reference `LiteDB` 6.0.0-prerelease.63.

```bash
dotnet add package Microsoft.SemanticKernel.Connectors.LiteDB --prerelease
dotnet add package LiteDB --version 6.0.0-prerelease.63
```

## Usage

```csharp
using LiteDB;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDB;

var connectionString = "Filename=vector.db;Connection=direct";
await using var store = new LiteDbVectorStore(connectionString);

var collection = store.GetCollection<string, ArticleRecord>("articles");
await collection.EnsureCollectionExistsAsync();

await collection.UpsertAsync(new ArticleRecord
{
    Id = "doc-1",
    Title = "LiteDB quick start",
    Embedding = embeddingVector
});

await foreach (var result in collection.SearchAsync(queryVector, top: 5))
{
    Console.WriteLine($"{result.Record.Id}: {result.Score}");
}

public sealed class ArticleRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Title { get; set; } = string.Empty;

    [VectorStoreVector(1536)]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
```

To register the connector via dependency injection:

```csharp
services.AddLiteDbVectorStore("Filename=vector.db;Connection=direct");
```

## Features

* Automatic creation of LiteDB vector indexes with configurable distance metrics.
* Support for equality, comparison, and logical filters over scalar metadata.
* Compatibility with Semantic Kernel embedding generators for automatic vector production.
* Async-friendly APIs matching the Semantic Kernel vector store abstractions.
