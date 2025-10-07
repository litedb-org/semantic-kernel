// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;
using Xunit;

namespace SemanticKernel.Connectors.LiteDb.UnitTests;

public sealed class LiteDbVectorStoreTests
{
    [Fact]
    public async Task UpsertAndGetRecordWithVectorsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        var record = new TestHotel
        {
            HotelId = "alpha",
            HotelName = "Alpha",
            DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
        };

        await collection.UpsertAsync(record);
        var fetched = await collection.GetAsync("alpha", new RecordRetrievalOptions { IncludeVectors = true });

        Assert.NotNull(fetched);
        Assert.Equal("Alpha", fetched!.HotelName);
        Assert.True(fetched.DescriptionEmbedding.HasValue);
        Assert.Equal(record.DescriptionEmbedding!.Value.ToArray(), fetched.DescriptionEmbedding!.Value.ToArray());
    }

    [Fact]
    public async Task VectorSearchReturnsNearestNeighborsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new TestHotel { HotelId = "alpha", HotelName = "Alpha", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f }) },
            new TestHotel { HotelId = "beta", HotelName = "Beta", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }) },
            new TestHotel { HotelId = "gamma", HotelName = "Gamma", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 0f, 1f }) }
        });

        var results = new List<VectorSearchResult<TestHotel>>();
        await foreach (var result in collection.SearchAsync(new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }), top: 2))
        {
            results.Add(result);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("beta", results[0].Record.HotelId);
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public async Task FilteredQueryReturnsExpectedRecordsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new TestHotel { HotelId = "alpha", HotelName = "Alpha", Rating = 4, City = "Seattle", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f }) },
            new TestHotel { HotelId = "beta", HotelName = "Beta", Rating = 3, City = "Portland", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }) },
            new TestHotel { HotelId = "gamma", HotelName = "Gamma", Rating = 5, City = "Seattle", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 0f, 1f }) }
        });

        var matches = new List<TestHotel>();
        await foreach (var item in collection.GetAsync(h => h.Rating >= 4, top: 5))
        {
            matches.Add(item);
        }

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.HotelId == "alpha");
        Assert.Contains(matches, m => m.HotelId == "gamma");
    }

    [Fact]
    public async Task EmbeddingGeneratorPopulatesVectorsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            EmbeddingGenerator = new StringEmbeddingGenerator()
        });

        var collection = store.GetCollection<string, GeneratedHotel>("generated_hotels");
        await collection.EnsureCollectionExistsAsync();

        var record = new GeneratedHotel
        {
            HotelId = "alpha",
            Description = "1,0,0"
        };

        await collection.UpsertAsync(record);

        var document = database.GetCollection("generated_hotels").FindById("alpha");

        Assert.NotNull(document);
        Assert.True(document.TryGetValue(nameof(GeneratedHotel.Description), out var value));
        Assert.IsType<BsonVector>(value);
        Assert.Equal(new[] { 1f, 0f, 0f }, ((BsonVector)value).Values);

        var searchResults = new List<VectorSearchResult<GeneratedHotel>>();
        await foreach (var result in collection.SearchAsync("1,0,0", top: 1))
        {
            searchResults.Add(result);
        }

        Assert.Single(searchResults);
        Assert.Equal("alpha", searchResults[0].Record.HotelId);
    }

    [Fact]
    public async Task SearchHonorsDistanceMetricsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var dotCollection = store.GetCollection<string, DotProductHotel>("dot_hotels");
        await dotCollection.EnsureCollectionExistsAsync();

        await dotCollection.UpsertAsync(new[]
        {
            new DotProductHotel { HotelId = "alpha", Embedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f }) },
            new DotProductHotel { HotelId = "beta", Embedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }) }
        });

        var dotResults = new List<VectorSearchResult<DotProductHotel>>();
        await foreach (var result in dotCollection.SearchAsync(new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }), top: 2))
        {
            dotResults.Add(result);
        }

        Assert.Equal("beta", dotResults[0].Record.HotelId);
        Assert.True(dotResults[0].Score >= dotResults[1].Score);

        var euclideanCollection = store.GetCollection<string, EuclideanHotel>("euclidean_hotels");
        await euclideanCollection.EnsureCollectionExistsAsync();

        await euclideanCollection.UpsertAsync(new[]
        {
            new EuclideanHotel { HotelId = "near", Embedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f }) },
            new EuclideanHotel { HotelId = "far", Embedding = new ReadOnlyMemory<float>(new[] { 2f, 0f, 0f }) }
        });

        var euclideanResults = new List<VectorSearchResult<EuclideanHotel>>();
        await foreach (var result in euclideanCollection.SearchAsync(new ReadOnlyMemory<float>(new[] { 0f, 0f, 0f }), top: 2))
        {
            euclideanResults.Add(result);
        }

        Assert.Equal("near", euclideanResults[0].Record.HotelId);
        Assert.True(euclideanResults[0].Score <= euclideanResults[1].Score);
    }

    [Fact]
    public async Task FilterSupportsLogicalOperatorsAndSkipAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new TestHotel { HotelId = "alpha", HotelName = "Alpha", Rating = 5, City = "Seattle", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f }) },
            new TestHotel { HotelId = "beta", HotelName = "Beta", Rating = 4, City = "Seattle", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f }) },
            new TestHotel { HotelId = "gamma", HotelName = "Gamma", Rating = 3, City = "Portland", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 0f, 1f }) },
            new TestHotel { HotelId = "delta", HotelName = "Delta", Rating = 4, City = "Portland", DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0.5f, 0.5f, 0f }) }
        });

        var options = new FilteredRecordRetrievalOptions<TestHotel>
        {
            Skip = 1
        };

        var filtered = new List<TestHotel>();
        await foreach (var item in collection.GetAsync(h => (h.City == "Seattle" && h.Rating >= 4) || h.City == "Portland", top: 3, options))
        {
            filtered.Add(item);
        }

        Assert.Equal(3, filtered.Count);
        Assert.DoesNotContain(filtered, h => h.HotelId == "alpha");
        Assert.Contains(filtered, h => h.HotelId == "beta");
        Assert.Contains(filtered, h => h.HotelId == "gamma");
        Assert.Contains(filtered, h => h.HotelId == "delta");
    }

    [Fact]
    public async Task DynamicCollectionRoundtripsRecordsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)),
                new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), 3)
            }
        };

        var collection = store.GetDynamicCollection("dynamic_hotels", definition);
        await collection.EnsureCollectionExistsAsync();

        var record = new Dictionary<string, object?>
        {
            ["Id"] = "alpha",
            ["Category"] = "city",
            ["Embedding"] = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
        };

        await collection.UpsertAsync(record);

        var fetched = await collection.GetAsync("alpha", new RecordRetrievalOptions { IncludeVectors = true });

        Assert.NotNull(fetched);
        Assert.Equal("city", fetched!["Category"]);
        var embedding = (ReadOnlyMemory<float>)fetched["Embedding"]!;
        Assert.Equal(new[] { 1f, 0f, 0f }, embedding.ToArray());
    }

    private sealed class TestHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreData(IsIndexed = true)]
        public string? HotelName { get; set; }

        [VectorStoreData]
        public int Rating { get; set; }

        [VectorStoreData]
        public string? City { get; set; }

        [VectorStoreVector(Dimensions: 3, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float>? DescriptionEmbedding { get; set; }
    }

    private sealed class GeneratedHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreVector(Dimensions: 3)]
        public string? Description { get; set; }
    }

    private sealed class DotProductHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreVector(Dimensions: 3, DistanceFunction = DistanceFunction.DotProductSimilarity)]
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }

    private sealed class EuclideanHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreVector(Dimensions: 3, DistanceFunction = DistanceFunction.EuclideanDistance)]
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }

    private sealed class StringEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var value in values)
            {
                var vector = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(float.Parse)
                    .ToArray();

                embeddings.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
