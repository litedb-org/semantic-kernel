// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;

namespace GettingStartedWithVectorStores;

/// <summary>
/// Demonstrates advanced LiteDB scenarios including transactional ingestion, per-property embedding generators, and filtered vector search.
/// </summary>
public sealed class Step5_LiteDb_Advanced(ITestOutputHelper output) : BaseTest(output)
{
    [Fact]
    public async Task RunLiteDbAdvancedScenarioAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var storeOptions = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            AutoCreateVectorIndexes = true,
            EmbeddingGenerator = new CsvEmbeddingGenerator()
        };

        using var store = new LiteDbVectorStore(database, storeOptions);

        var definition = new VectorStoreCollectionDefinition
        {
            EmbeddingGenerator = storeOptions.EmbeddingGenerator,
            Properties =
            {
                new VectorStoreKeyProperty(nameof(HotelRecord.HotelId), typeof(string)),
                new VectorStoreDataProperty(nameof(HotelRecord.City), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(HotelRecord.Tags), typeof(string[])),
                new VectorStoreDataProperty(nameof(HotelRecord.Rating), typeof(int)),
                new VectorStoreVectorProperty<string>(nameof(HotelRecord.Description), 3),
                new VectorStoreVectorProperty<string>(nameof(HotelRecord.Amenities), 3)
                {
                    DistanceFunction = DistanceFunction.DotProductSimilarity,
                    EmbeddingGenerator = new KeywordEmbeddingGenerator()
                }
            }
        };

        var collection = store.GetCollection<string, HotelRecord>("hotels", definition);
        await collection.EnsureCollectionExistsAsync();

        var hotels = new[]
        {
            new HotelRecord { HotelId = "alpha", City = "Seattle", Rating = 5, Tags = new[] { "spa", "downtown" }, Description = "1,0,0", Amenities = "spa gym pool" },
            new HotelRecord { HotelId = "beta", City = "Portland", Rating = 4, Tags = new[] { "spa", "budget" }, Description = "0,1,0", Amenities = "spa" },
            new HotelRecord { HotelId = "gamma", City = "Chicago", Rating = 3, Tags = new[] { "business" }, Description = "0,0,1", Amenities = "conference" }
        };

        // Batched upsert operations are wrapped in a LiteDB transaction to ensure all-or-nothing writes.
        await collection.UpsertAsync(hotels);

        var searchOptions = new VectorSearchOptions<HotelRecord>
        {
            VectorProperty = h => h.Amenities,
            Filter = h =>
                ((h.Tags != null && h.Tags.Contains("spa")) || h.City!.StartsWith("Sea")) &&
                new[] { "Seattle", "Portland" }.Contains(h.City!) &&
                h.Rating >= 4
        };

        await foreach (var result in collection.SearchAsync("spa gym pool", top: 2, searchOptions))
        {
            this.Output.WriteLine($"{result.Record.HotelId}: {result.Score:F2}");
        }
    }

    private sealed class HotelRecord
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreData]
        public string? City { get; set; }

        [VectorStoreData]
        public string[]? Tags { get; set; }

        [VectorStoreData]
        public int Rating { get; set; }

        [VectorStoreVector(Dimensions: 3)]
        public string? Description { get; set; }

        [VectorStoreVector(Dimensions: 3)]
        public string? Amenities { get; set; }
    }

    private sealed class CsvEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
            {
                embeddings.Add(Parse(value));
            }

            return Task.FromResult(embeddings);
        }

        public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Parse(value));

        private static Embedding<float> Parse(string value)
        {
            var numbers = value.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Select(segment => float.TryParse(segment, out var parsed) ? parsed : 0f)
                .ToArray();
            return new Embedding<float>(numbers);
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class KeywordEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly string[] Vocabulary = ["spa", "gym", "pool"];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
            {
                embeddings.Add(CreateEmbedding(value));
            }

            return Task.FromResult(embeddings);
        }

        public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateEmbedding(value));

        private static Embedding<float> CreateEmbedding(string value)
        {
            var tokens = value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var vector = Vocabulary.Select(v => tokens.Count(t => string.Equals(t, v, System.StringComparison.OrdinalIgnoreCase))).Select(count => (float)count).ToArray();
            return new Embedding<float>(vector);
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
