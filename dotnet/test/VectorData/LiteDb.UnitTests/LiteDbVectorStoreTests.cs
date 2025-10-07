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
using Microsoft.Extensions.DependencyInjection;
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
        var first = results[0];
        var second = results[1];
        Assert.NotNull(first.Record);
        var firstRecord = first.Record!;
        Assert.NotNull(firstRecord.HotelId);
        var firstHotelId = firstRecord.HotelId!;
        Assert.Equal("beta", firstHotelId);
        Assert.True(first.Score >= second.Score);
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

    [Fact]
    public async Task CollectionPrefixIsAppliedToStorageNameAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        database.GetCollection<BsonDocument>("legacy").Insert(new BsonDocument { ["_id"] = "legacy" });

        var options = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            CollectionNamePrefix = "sk_"
        };

        using var store = new LiteDbVectorStore(database, options);

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        var names = database.GetCollectionNames().ToArray();
        Assert.Contains("sk_hotels", names);
        Assert.Equal("sk_hotels", collection.Name);

        var logicalNames = new List<string>();
        await foreach (var name in store.ListCollectionNamesAsync())
        {
            logicalNames.Add(name);
        }

        Assert.Contains("hotels", logicalNames);
        Assert.DoesNotContain("sk_hotels", logicalNames);
        Assert.DoesNotContain("legacy", logicalNames);
        Assert.True(await store.CollectionExistsAsync("hotels"));

        await store.EnsureCollectionDeletedAsync("hotels");
        names = database.GetCollectionNames().ToArray();
        Assert.DoesNotContain("sk_hotels", names);
    }

    [Fact]
    public async Task DatabaseFactoryRespectsDisposeFlagAsync()
    {
        var stream = new MemoryStream();
        var callCount = 0;

        var options = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            DatabaseFactory = () =>
            {
                callCount++;
                return new LiteDatabase(stream);
            }
        };

        using (var store = new LiteDbVectorStore(options))
        {
            var collection = store.GetCollection<string, TestHotel>("hotels");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(new TestHotel
            {
                HotelId = "alpha",
                HotelName = "Alpha",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
            });

            var fetched = await collection.GetAsync("alpha");
            Assert.NotNull(fetched);
        }

        Assert.Equal(1, callCount);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ServiceCollectionRegistersLiteDbStoreAndCollectionsAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator, StringEmbeddingGenerator>();
        services.AddLiteDbVectorStore(sp => new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            DatabaseFactory = () => new LiteDatabase(new MemoryStream())
        });
        services.AddLiteDbCollection<string, TestHotel>("hotels");
        services.AddLiteDbCollection<string, GeneratedHotel>("generated_hotels");

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<VectorStore>();
        var liteStore = Assert.IsType<LiteDbVectorStore>(store);

        var hotelCollection = provider.GetRequiredService<VectorStoreCollection<string, TestHotel>>();
        await hotelCollection.EnsureCollectionExistsAsync();

        await hotelCollection.UpsertAsync(new TestHotel
        {
            HotelId = "alpha",
            HotelName = "Alpha",
            DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
        });

        Assert.NotNull(await hotelCollection.GetAsync("alpha"));

        var generatedCollection = provider.GetRequiredService<VectorStoreCollection<string, GeneratedHotel>>();
        await generatedCollection.EnsureCollectionExistsAsync();

        await generatedCollection.UpsertAsync(new GeneratedHotel
        {
            HotelId = "beta",
            Description = "1,0,0"
        });

        var searchResults = new List<VectorSearchResult<GeneratedHotel>>();
        await foreach (var result in generatedCollection.SearchAsync("1,0,0", top: 1))
        {
            searchResults.Add(result);
        }

        Assert.Single(searchResults);
        Assert.Equal("beta", searchResults[0].Record.HotelId);

        var collectionNames = new List<string>();
        await foreach (var name in liteStore.ListCollectionNamesAsync())
        {
            collectionNames.Add(name);
        }

        Assert.Contains("hotels", collectionNames);
        Assert.Contains("generated_hotels", collectionNames);
    }

    [Fact]
    public async Task FilterSupportsStringMembershipAndNestedGroupsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TaggedHotel>("tagged_hotels");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new TaggedHotel
            {
                HotelId = "alpha",
                City = "Seattle",
                Rating = 5,
                Tags = new[] { "spa", "downtown" },
                Description = "Coastline Inn",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
            },
            new TaggedHotel
            {
                HotelId = "beta",
                City = "Portland",
                Rating = 4,
                Tags = new[] { "business" },
                Description = "City Center Hotel",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f })
            },
            new TaggedHotel
            {
                HotelId = "gamma",
                City = "Seattle",
                Rating = 3,
                Tags = new[] { "historic" },
                Description = "Harbor Inn",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 0f, 1f })
            }
        });

        var results = new List<TaggedHotel>();
        var allowedCities = new[] { "Seattle", "Portland" };
        await foreach (var record in collection.GetAsync(
            h => (h.Tags.Contains("spa") || h.City!.StartsWith("Sea"))
                && allowedCities.Contains(h.City!)
                && h.Description!.EndsWith("Inn")
                && h.Rating >= 4,
            top: 5))
        {
            results.Add(record);
        }

        var result = Assert.Single(results);
        Assert.NotNull(result.HotelId);
        var hotelId = result.HotelId!;
        Assert.Equal("alpha", hotelId);
    }

    [Fact]
    public async Task BatchUpsertRollsBackOnFailureAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        var rawCollection = database.GetCollection<BsonDocument>("hotels");
        rawCollection.EnsureIndex(nameof(TestHotel.HotelName), unique: true);

        var records = new[]
        {
            new TestHotel
            {
                HotelId = "alpha",
                HotelName = "Duplicate",
                Rating = 4,
                City = "Seattle",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
            },
            new TestHotel
            {
                HotelId = "beta",
                HotelName = "Duplicate",
                Rating = 5,
                City = "Portland",
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f })
            }
        };

        await Assert.ThrowsAsync<LiteException>(() => collection.UpsertAsync(records));

        Assert.Empty(rawCollection.FindAll());
    }

    [Fact]
    public async Task ThrowsWhenVectorDimensionsMismatchAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions { DisposeDatabase = false });

        var collection = store.GetCollection<string, TestHotel>("hotels");
        await collection.EnsureCollectionExistsAsync();

        var invalid = new TestHotel
        {
            HotelId = "alpha",
            HotelName = "Alpha",
            City = "Seattle",
            Rating = 4,
            DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f })
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => collection.UpsertAsync(invalid));
        Assert.Contains("expects 3 dimensions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropertyAndCollectionMetricsTakePrecedenceOverStoreDefaultsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());

        var storeOptions = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            DistanceMetric = LiteDbDistanceMetric.Cosine
        };

        var collectionOptions = new LiteDbCollectionOptions
        {
            DistanceMetric = LiteDbDistanceMetric.Euclidean
        };

        using var collection = new LiteDbCollection<string, MultiVectorHotel>(
            database,
            "multi_hotels",
            storeOptions,
            collectionOptions,
            opts => new LiteDbModelBuilder().Build(typeof(MultiVectorHotel), opts.Definition, opts.EmbeddingGenerator),
            connectionIdentifier: "test");

        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new MultiVectorHotel
            {
                HotelId = "alpha",
                AmenitiesEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f }),
                LocationEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 10f })
            },
            new MultiVectorHotel
            {
                HotelId = "beta",
                AmenitiesEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 1f }),
                LocationEmbedding = new ReadOnlyMemory<float>(new[] { 0f, 2f })
            }
        });

        var amenityResults = new List<VectorSearchResult<MultiVectorHotel>>();
        var amenityOptions = new VectorSearchOptions<MultiVectorHotel>
        {
            VectorProperty = h => h.AmenitiesEmbedding
        };

        await foreach (var result in collection.SearchAsync(new ReadOnlyMemory<float>(new[] { 0f, 1f }), top: 1, amenityOptions))
        {
            amenityResults.Add(result);
        }

        Assert.Single(amenityResults);
        Assert.Equal("beta", amenityResults[0].Record.HotelId);

        var locationResults = new List<VectorSearchResult<MultiVectorHotel>>();
        var locationOptions = new VectorSearchOptions<MultiVectorHotel>
        {
            VectorProperty = h => h.LocationEmbedding
        };

        await foreach (var result in collection.SearchAsync(new ReadOnlyMemory<float>(new[] { 0f, 0f }), top: 1, locationOptions))
        {
            locationResults.Add(result);
        }

        Assert.Single(locationResults);
        Assert.Equal("beta", locationResults[0].Record.HotelId);
    }

    [Fact]
    public async Task PropertySpecificEmbeddingGeneratorOverridesDefaultsAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());

        var defaultGenerator = new TrackingStringEmbeddingGenerator(value => new[] { 9f, 9f, 9f });
        var overrideGenerator = new TrackingStringEmbeddingGenerator(ParseVector);

        var storeOptions = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            EmbeddingGenerator = defaultGenerator
        };

        using var store = new LiteDbVectorStore(database, storeOptions);

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty(nameof(OverrideHotel.HotelId), typeof(string)),
                new VectorStoreVectorProperty<string>(nameof(OverrideHotel.Overview), 3)
                {
                    EmbeddingGenerator = overrideGenerator
                },
                new VectorStoreVectorProperty<string>(nameof(OverrideHotel.Amenities), 3)
            }
        };

        var collection = store.GetCollection<string, OverrideHotel>("override_hotels", definition);
        await collection.EnsureCollectionExistsAsync();

        var record = new OverrideHotel
        {
            HotelId = "alpha",
            Overview = "1,0,0",
            Amenities = "0,1,0"
        };

        await collection.UpsertAsync(record);

        var raw = database.GetCollection("override_hotels").FindById("alpha");
        Assert.NotNull(raw);
        Assert.Equal(new[] { 1f, 0f, 0f }, ((BsonVector)raw![nameof(OverrideHotel.Overview)]).Values);
        Assert.Equal(new[] { 9f, 9f, 9f }, ((BsonVector)raw![nameof(OverrideHotel.Amenities)]).Values);

        Assert.Equal(1, overrideGenerator.BatchCalls);
        Assert.Single(overrideGenerator.BatchInputs);
        Assert.Equal("1,0,0", overrideGenerator.BatchInputs[0]);
        Assert.Equal(1, defaultGenerator.BatchCalls);
        Assert.Single(defaultGenerator.BatchInputs);
        Assert.Equal("0,1,0", defaultGenerator.BatchInputs[0]);

        var options = new VectorSearchOptions<OverrideHotel>
        {
            VectorProperty = h => h.Overview
        };

        var searchResults = new List<VectorSearchResult<OverrideHotel>>();
        await foreach (var result in collection.SearchAsync("1,0,0", top: 1, options))
        {
            searchResults.Add(result);
        }

        Assert.Single(searchResults);
        Assert.Equal("alpha", searchResults[0].Record.HotelId);
    }

    [Fact]
    public async Task CancellationStopsEmbeddingGenerationOnUpsertAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var generator = new CancellableEmbeddingGenerator();

        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            EmbeddingGenerator = generator
        });

        var collection = store.GetCollection<string, GeneratedHotel>("generated_hotels");
        await collection.EnsureCollectionExistsAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => collection.UpsertAsync(new GeneratedHotel
        {
            HotelId = "alpha",
            Description = "1,0,0"
        }, cts.Token));

        Assert.Null(database.GetCollection("generated_hotels").FindById("alpha"));
    }

    [Fact]
    public async Task CancellationStopsEmbeddingGenerationOnSearchAsync()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var generator = new CancellableEmbeddingGenerator();

        using var store = new LiteDbVectorStore(database, new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            EmbeddingGenerator = generator
        });

        var collection = store.GetCollection<string, GeneratedHotel>("generated_hotels");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new GeneratedHotel
        {
            HotelId = "alpha",
            Description = "1,0,0"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in collection.SearchAsync("1,0,0", top: 1, cancellationToken: cts.Token))
            {
            }
        });
    }

    [Fact]
    public void AutoCreateVectorIndexesAliasKeepsOptionsInSync()
    {
        var options = new LiteDbVectorStoreOptions
        {
            AutoCreateVectorIndexes = false
        };

        Assert.False(options.AutoEnsureVectorIndex);

        options.AutoEnsureVectorIndex = true;
        Assert.True(options.AutoCreateVectorIndexes);

        options.AutoCreateVectorIndexes = false;
        Assert.False(options.AutoEnsureVectorIndex);
    }

    [Fact]
    public async Task DatabaseFactoryHasHighestPrecedenceAsync()
    {
        var providedStream = new MemoryStream();
        using var providedDatabase = new LiteDatabase(providedStream);

        LiteDatabase? factoryDatabase = null;
        var options = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            Database = providedDatabase,
            DatabaseFactory = () =>
            {
                factoryDatabase = new LiteDatabase(new MemoryStream());
                return factoryDatabase;
            }
        };

        using (var store = new LiteDbVectorStore(options))
        {
            var collection = store.GetCollection<string, TestHotel>("hotels");
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new TestHotel
            {
                HotelId = "alpha",
                HotelName = "Alpha",
                City = "Seattle",
                Rating = 4,
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
            });
        }

        Assert.NotNull(factoryDatabase);
        Assert.NotNull(factoryDatabase!.GetCollection("hotels").FindById("alpha"));
        Assert.Null(providedDatabase.GetCollection("hotels").FindById("alpha"));
    }

    [Fact]
    public async Task DatabaseInstanceOverridesConnectionStringAsync()
    {
        using var providedDatabase = new LiteDatabase(new MemoryStream());
        var options = new LiteDbVectorStoreOptions
        {
            DisposeDatabase = false,
            Database = providedDatabase,
            ConnectionString = "Filename=ignored.db"
        };

        using (var store = new LiteDbVectorStore("Filename=also-ignored.db", options))
        {
            var collection = store.GetCollection<string, TestHotel>("hotels");
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new TestHotel
            {
                HotelId = "alpha",
                HotelName = "Alpha",
                City = "Seattle",
                Rating = 4,
                DescriptionEmbedding = new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })
            });
        }

        Assert.NotNull(providedDatabase.GetCollection("hotels").FindById("alpha"));
    }

    private static float[] ParseVector(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(float.Parse)
            .ToArray();

    private sealed class TaggedHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreData]
        public string[] Tags { get; set; } = Array.Empty<string>();

        [VectorStoreData]
        public string? City { get; set; }

        [VectorStoreData]
        public int Rating { get; set; }

        [VectorStoreData]
        public string? Description { get; set; }

        [VectorStoreVector(Dimensions: 3)]
        public ReadOnlyMemory<float>? DescriptionEmbedding { get; set; }
    }

    private sealed class MultiVectorHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        [VectorStoreVector(Dimensions: 2, DistanceFunction = DistanceFunction.DotProductSimilarity)]
        public ReadOnlyMemory<float>? AmenitiesEmbedding { get; set; }

        [VectorStoreVector(Dimensions: 2)]
        public ReadOnlyMemory<float>? LocationEmbedding { get; set; }
    }

    private sealed class OverrideHotel
    {
        [VectorStoreKey]
        public string? HotelId { get; set; }

        public string? Overview { get; set; }

        public string? Amenities { get; set; }
    }

    private sealed class TrackingStringEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly Func<string, float[]> _projection;

        internal TrackingStringEmbeddingGenerator(Func<string, float[]> projection)
        {
            this._projection = projection;
        }

        public int BatchCalls { get; private set; }

        public int SingleCalls { get; private set; }

        public List<string> BatchInputs { get; } = new();

        public List<string> SingleInputs { get; } = new();

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.BatchCalls++;

            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.BatchInputs.Add(value);
                embeddings.Add(new Embedding<float>(this._projection(value)));
            }

            return Task.FromResult(embeddings);
        }

        public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.SingleCalls++;
            this.SingleInputs.Add(value);
            return Task.FromResult(new Embedding<float>(this._projection(value)));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class CancellableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                embeddings.Add(new Embedding<float>(ParseVector(value)));
            }

            return Task.FromResult(embeddings);
        }

        public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Embedding<float>(ParseVector(value)));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
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
