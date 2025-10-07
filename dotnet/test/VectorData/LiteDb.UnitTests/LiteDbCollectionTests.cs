// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;
using Xunit;

namespace SemanticKernel.Connectors.LiteDb.UnitTests;

public sealed class LiteDbCollectionTests : IDisposable
{
    private readonly string _databasePath;
    private readonly List<LiteDbVectorStore> _stores = new();

    public LiteDbCollectionTests()
    {
        this._databasePath = Path.Combine(Path.GetTempPath(), $"sk-lite-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task UpsertAndRetrieveRecordAsync()
    {
        var collection = await this.CreateCollectionAsync();

        var record = new TestRecord
        {
            Id = "item-1",
            Title = "First",
            Tags = ["alpha"],
            Embedding = new float[] { 0.1f, 0.2f, 0.3f }
        };

        await collection.UpsertAsync(record);

        var retrieved = await collection.GetAsync("item-1", new RecordRetrievalOptions { IncludeVectors = true });

        Assert.NotNull(retrieved);
        Assert.Equal("First", retrieved!.Title);
        Assert.Equal(record.Tags, retrieved.Tags);
        Assert.True(retrieved.Embedding.Span.SequenceEqual(record.Embedding.Span));
    }

    [Fact]
    public async Task SearchReturnsResultsOrderedBySimilarityAsync()
    {
        var collection = await this.CreateCollectionAsync();

        await collection.UpsertAsync(new TestRecord
        {
            Id = "item-1",
            Title = "Alpha",
            Tags = ["a"],
            Embedding = new float[] { 0.0f, 0.0f, 1.0f }
        });

        await collection.UpsertAsync(new TestRecord
        {
            Id = "item-2",
            Title = "Beta",
            Tags = ["b"],
            Embedding = new float[] { 1.0f, 0.0f, 0.0f }
        });

        await collection.UpsertAsync(new TestRecord
        {
            Id = "item-3",
            Title = "Gamma",
            Tags = ["c"],
            Embedding = new float[] { 0.0f, 1.0f, 0.0f }
        });

        var results = new List<VectorSearchResult<TestRecord>>();
        await foreach (var result in collection.SearchAsync(new float[] { 0.0f, 0.0f, 1.0f }, 2))
        {
            results.Add(result);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("item-1", results[0].Record.Id);
        Assert.Equal("item-2", results[1].Record.Id);
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public async Task ListCollectionNamesRespectsPrefix()
    {
        var options = new LiteDbVectorStoreOptions
        {
            ConnectionString = $"Filename={this._databasePath};Connection=direct",
            CollectionNamePrefix = "sk_"
        };

        using var store = new LiteDbVectorStore(options);
        var collection = store.GetCollection<string, TestRecord>("vectors");
        await collection.EnsureCollectionExistsAsync();

        await foreach (var name in store.ListCollectionNamesAsync())
        {
            Assert.Equal("vectors", name);
        }
    }

    public void Dispose()
    {
        foreach (var store in this._stores)
        {
            store.Dispose();
        }

        try
        {
            if (File.Exists(this._databasePath))
            {
                File.Delete(this._databasePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<LiteDbCollection<string, TestRecord>> CreateCollectionAsync()
    {
        var options = new LiteDbVectorStoreOptions
        {
            ConnectionString = $"Filename={this._databasePath};Connection=direct"
        };

        var store = new LiteDbVectorStore(options);
        this._stores.Add(store);
        var collection = (LiteDbCollection<string, TestRecord>)store.GetCollection<string, TestRecord>("records");
        await collection.EnsureCollectionExistsAsync();
        return collection;
    }

    public sealed class TestRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string Title { get; set; } = string.Empty;

        [VectorStoreData]
        public List<string> Tags { get; set; } = new();

        [VectorStoreVector(3, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
