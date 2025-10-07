// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;

namespace GettingStartedWithVectorStores;

/// <summary>
/// Demonstrates advanced LiteDB usage: per-property generators and metrics, transactional batch upserts, and expressive filters.
/// </summary>
public sealed class Step5_LiteDb_AdvancedScenario(ITestOutputHelper output) : BaseTest(output)
{
    private static readonly string[] FilterCities = new[] { "Seattle", "Portland" };

    [Fact]
    public async Task RunEndToEndScenarioAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new SampleEmbeddingGenerator("store"));
        services.AddLiteDbVectorStore(_ => new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            DistanceMetric = LiteDbDistanceMetric.Cosine,
            AutoCreateVectorIndexes = true,
            CollectionNamePrefix = "sample_"
        });
        services.AddLiteDbCollection<string, SampleHotel>("hotels", _ => new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty(nameof(SampleHotel.HotelId), typeof(string)),
                new VectorStoreDataProperty(nameof(SampleHotel.City), typeof(string)),
                new VectorStoreDataProperty(nameof(SampleHotel.Tags), typeof(List<string>)),
                new VectorStoreDataProperty(nameof(SampleHotel.Description), typeof(string)),
                new VectorStoreVectorProperty(nameof(SampleHotel.Overview), typeof(string), 3)
                {
                    DistanceFunction = DistanceFunction.DotProductSimilarity,
                    EmbeddingGenerator = new SampleEmbeddingGenerator("overview")
                },
                new VectorStoreVectorProperty(nameof(SampleHotel.Amenities), typeof(string), 3)
                {
                    DistanceFunction = DistanceFunction.EuclideanDistance,
                    EmbeddingGenerator = new SampleEmbeddingGenerator("amenities")
                }
            }
        });

        using var provider = services.BuildServiceProvider();
        var collection = provider.GetRequiredService<VectorStoreCollection<string, SampleHotel>>();
        await collection.EnsureCollectionExistsAsync();

        var hotels = new[]
        {
            new SampleHotel
            {
                HotelId = "alpha",
                City = "Seattle",
                Description = "Waterfront spa resort",
                Tags = new List<string> { "spa", "rooftop" },
                Overview = "0.9,0.05,0.05",
                Amenities = "0.2,0.5,0.3"
            },
            new SampleHotel
            {
                HotelId = "beta",
                City = "Portland",
                Description = "Modern downtown escape",
                Tags = new List<string> { "boutique", "spa" },
                Overview = "0.7,0.1,0.2",
                Amenities = "0.6,0.2,0.2"
            },
            new SampleHotel
            {
                HotelId = "gamma",
                City = "San Francisco",
                Description = "Historic harbor view",
                Tags = new List<string> { "historic", "view" },
                Overview = "0.1,0.8,0.1",
                Amenities = "0.3,0.1,0.6"
            }
        };

        // Batch upsert executes inside a transaction – either all records are stored or none are.
        await collection.UpsertAsync(hotels);

#pragma warning disable CA1866 // the literal "t" is clearer for sample filtering.
        var searchOptions = new VectorSearchOptions<SampleHotel>
        {
            VectorProperty = h => h.Overview,
            Filter = h => (h.Tags.Contains("spa") || h.City!.StartsWith("Sea"))
                && FilterCities.Contains(h.City!)
                && h.Description!.EndsWith("t"),
            IncludeVectors = false
        };
#pragma warning restore CA1866

        var results = await collection.SearchAsync("0.8,0.1,0.1", top: 2, searchOptions).ToListAsync();

        foreach (var result in results)
        {
            this.WriteLine($"Hotel: {result.Record.HotelId} in {result.Record.City} (score {result.Score:F3})");
        }

        Assert.Single(results);
        Assert.Equal("alpha", results[0].Record.HotelId);
    }

    private sealed class SampleHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreData]
        public string? City { get; set; }

        [VectorStoreData]
        public string? Description { get; set; }

        [VectorStoreData]
        public List<string> Tags { get; set; } = new();

        [VectorStoreVector(Dimensions: 3)]
        public string? Overview { get; set; }

        [VectorStoreVector(Dimensions: 3)]
        public string? Amenities { get; set; }
    }

    private sealed class SampleEmbeddingGenerator(string tag) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
            {
                embeddings.Add(new Embedding<float>(ParseVector(value, tag)));
            }

            return Task.FromResult(embeddings);
        }

        public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new Embedding<float>(ParseVector(value, tag)));

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }

        private static float[] ParseVector(string value, string tagPrefix)
        {
            var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(float.Parse)
                .ToArray();

            // Different prefixes allow callers to verify generator precedence in diagnostics.
            return tagPrefix switch
            {
                "overview" => values,
                _ => values.Select(v => Math.Clamp(v + 0.05f, 0f, 1f)).ToArray()
            };
        }
    }
}
