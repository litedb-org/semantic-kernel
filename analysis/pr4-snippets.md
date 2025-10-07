# PR #4 Reference Snippets

## LiteDbFilterTranslator string and membership support
```csharp
    }

    private string TranslateMethodCall(MethodCallExpression methodCall)
    {
        if (methodCall.Method.DeclaringType == typeof(string)
            && methodCall.Method.Name is nameof(string.Contains) or nameof(string.StartsWith) or nameof(string.EndsWith)
            && methodCall.Object is { } stringObject
            && this.TryBindProperty(stringObject, out var stringProperty))
        {
            return this.TranslateStringMethod(methodCall, stringProperty);
        }

        if (methodCall is { Method.Name: nameof(Enumerable.Contains), Method.DeclaringType: var declaringType } enumerableCall
            && declaringType == typeof(Enumerable))
        {
            return this.TranslateEnumerableContains(enumerableCall);
        }

        if (methodCall.Method.Name == nameof(List<int>.Contains)
            && methodCall.Object is not null
            && this.TryBindProperty(methodCall.Object, out var collectionProperty))
        {
            return this.TranslateCollectionContains(methodCall, collectionProperty);
        }

        if (methodCall.Method.Name == "Contains"
            && methodCall.Object is not null
            && methodCall.Method.DeclaringType is { } collectionDeclaringType
            && collectionDeclaringType != typeof(string)
            && this.TryBindProperty(methodCall.Object, out collectionProperty))
        {
            return this.TranslateCollectionContains(methodCall, collectionProperty);
        }

        throw new NotSupportedException($"Unsupported method call '{methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name}' in LiteDB filter expression.");
    }

    private string TranslateStringMethod(MethodCallExpression methodCall, PropertyModel property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        if (propertyType != typeof(string))
        {
            throw new NotSupportedException($"String method '{methodCall.Method.Name}' is only supported on string properties, but '{property.ModelName}' is of type '{property.Type.Name}'.");
        }

        if (methodCall.Arguments.Count == 0)
        {
            throw new NotSupportedException($"Method '{methodCall.Method.Name}' on property '{property.ModelName}' must specify a value argument.");
        }

        if (methodCall.Arguments.Count > 1)
        {
            throw new NotSupportedException($"LiteDB filters only support the overload of '{methodCall.Method.Name}' with a single value argument.");
        }

        if (!this.TryExtractConstant(methodCall.Arguments[0], out var value) || value is not string stringValue)
        {
            throw new NotSupportedException($"LiteDB filters require '{methodCall.Method.Name}' arguments to be constant strings.");
        }

        var pattern = methodCall.Method.Name switch
        {
            nameof(string.Contains) => $"%{EscapeLikePattern(stringValue)}%",
            nameof(string.StartsWith) => $"{EscapeLikePattern(stringValue)}%",
            nameof(string.EndsWith) => $"%{EscapeLikePattern(stringValue)}",
            _ => throw new NotSupportedException($"Unsupported string method '{methodCall.Method.Name}'.")
        };

        var placeholder = this.AddParameter(new BsonValue(pattern));
        var field = GetField(property);
        return $"({field} LIKE {placeholder})";
    }

    private string TranslateCollectionContains(MethodCallExpression methodCall, PropertyModel property)
    {
        if (!typeof(IEnumerable).IsAssignableFrom((Nullable.GetUnderlyingType(property.Type) ?? property.Type)))
        {
            throw new NotSupportedException($"Collection.Contains is only supported for enumerable properties. Property '{property.ModelName}' has type '{property.Type.Name}'.");
        }

        if (methodCall.Arguments.Count != 1)
        {
            throw new NotSupportedException($"Method '{methodCall.Method.Name}' must have exactly one argument in LiteDB filters.");
        }

        if (!this.TryExtractConstant(methodCall.Arguments[0], out var value))
        {
            throw new NotSupportedException("LiteDB filters require collection.Contains arguments to be constant values.");
        }

        var placeholder = this.AddParameter(this.CreateParameterValue(value));
        var field = GetField(property);
        return $"({field} ANY = {placeholder})";
    }

    private string TranslateEnumerableContains(MethodCallExpression methodCall)
    {
        if (methodCall.Arguments.Count != 2)
        {
            throw new NotSupportedException("Enumerable.Contains must specify the source and the item to compare.");
        }

        var source = methodCall.Arguments[0];
        var item = methodCall.Arguments[1];

        if (this.TryBindProperty(source, out var collectionProperty))
        {
            var propertyType = Nullable.GetUnderlyingType(collectionProperty.Type) ?? collectionProperty.Type;
            if (!typeof(IEnumerable).IsAssignableFrom(propertyType))
            {
                throw new NotSupportedException($"Enumerable.Contains is only supported on enumerable properties. Property '{collectionProperty.ModelName}' has type '{collectionProperty.Type.Name}'.");
            }

            if (!this.TryExtractConstant(item, out var element))
            {
                throw new NotSupportedException("LiteDB filters require Enumerable.Contains item arguments to be constant values when the source is a record property.");
            }

            var collectionPlaceholder = this.AddParameter(this.CreateParameterValue(element));
            var collectionField = GetField(collectionProperty);
            return $"({collectionField} ANY = {collectionPlaceholder})";
```

## LiteDbCollection vector validation and transactional upsert excerpts
```csharp
        var documents = new List<BsonDocument>(materialized.Count);
        for (var i = 0; i < materialized.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = this._mapper.MapToDocument(materialized[i], generatedVectors, i);
            this.ValidateVectorDimensions(document);
            documents.Add(document);
        }

        if (documents.Count == 1)
        {
            this._collection.Upsert(documents[0]);
            return;
        }

        var startedTransaction = this._database.BeginTrans();
        try
        {
            this._collection.Upsert(documents);
            if (startedTransaction)
            {
                this._database.Commit();
            }
        }
        catch
        {
            if (startedTransaction)
            {
                this._database.Rollback();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (top <= 0)
        {
                throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(value?.GetType() ?? typeof(object), LiteDbModelBuilder.SupportedVectorTypes));
        }
    }

    private static LiteDbDistanceMetric ResolveMetric(string? distanceFunction, LiteDbDistanceMetric fallback)
        => distanceFunction switch
        {
            DistanceFunction.CosineSimilarity => LiteDbDistanceMetric.Cosine,
            DistanceFunction.CosineDistance => LiteDbDistanceMetric.Cosine,
            DistanceFunction.DotProductSimilarity => LiteDbDistanceMetric.DotProduct,
            DistanceFunction.EuclideanDistance => LiteDbDistanceMetric.Euclidean,
            _ => fallback
        };

    private static VectorDistanceMetric MapMetric(LiteDbDistanceMetric metric)
        => metric switch
        {
            LiteDbDistanceMetric.Cosine => VectorDistanceMetric.Cosine,
            LiteDbDistanceMetric.DotProduct => VectorDistanceMetric.DotProduct,
            LiteDbDistanceMetric.Euclidean => VectorDistanceMetric.Euclidean,
            _ => throw new NotSupportedException($"Unsupported distance metric '{metric}'.")
        };

    private void ValidateVectorDimensions(BsonDocument document)
    {
        foreach (var property in this._vectorProperties)
        {
            if (!document.TryGetValue(property.StorageName, out var value) || value is not BsonVector vector)
            {
                continue;
            }

            var expectedDimensions = this._options.VectorDimensions ?? property.Dimensions;
            if (expectedDimensions <= 0)
            {
                continue;
            }

            if (vector.Values.Length != expectedDimensions)
            {
                throw new InvalidOperationException($"Vector property '{property.ModelName}' expects {expectedDimensions} dimensions but received {vector.Values.Length}.");
            }
        }
    }
}
```

## LiteDbFilterTranslator unit tests
```csharp
    [Fact]
    public void TranslatesStringAndMembershipOperators()
    {
        var builder = new LiteDbModelBuilder();
        var model = builder.Build(typeof(FilterHotel), definition: null, defaultEmbeddingGenerator: null);
        Expression<Func<FilterHotel, bool>> filter = h =>
            (h.Tags.Contains("spa") || h.City.StartsWith("Sea"))
            && new[] { "Seattle", "Portland" }.Contains(h.City)
            && h.Description.EndsWith("Inn");

        var translator = new LiteDbFilterTranslator();
        var (expression, parameters) = translator.Translate(filter, model);

        Assert.Equal("(((($.Tags ANY = @0) OR ($.City LIKE @1)) AND ($.City IN @2)) AND ($.Description LIKE @3))", expression);
        Assert.Collection(parameters,
            p => Assert.Equal("spa", p.AsString),
            p => Assert.Equal("Sea%", p.AsString),
            p =>
            {
                var array = p.AsArray;
                Assert.Equal(2, array.Count);
                Assert.Equal("Seattle", array[0].AsString);
                Assert.Equal("Portland", array[1].AsString);
            },
            p => Assert.Equal("%Inn", p.AsString));
    }

    [Fact]
    public void ThrowsHelpfulErrorForUnsupportedStringComparison()
    {
        var builder = new LiteDbModelBuilder();
        var model = builder.Build(typeof(FilterHotel), definition: null, defaultEmbeddingGenerator: null);
        Expression<Func<FilterHotel, bool>> filter = h => h.City.StartsWith("Sea", StringComparison.OrdinalIgnoreCase);

        var translator = new LiteDbFilterTranslator();
        var exception = Assert.Throws<NotSupportedException>(() => translator.Translate(filter, model));
        Assert.Contains("single value argument", exception.Message);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via reflection during model binding.")]
    private sealed class FilterHotel
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
```

## Transaction rollback and dimension validation tests
```csharp
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

```

## Embedding generator overrides and cancellation tests
```csharp
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
```

## Options alias and precedence tests
```csharp
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
```

## README additions
```markdown
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

```

## Sample scenario
```csharp
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
```

## Metric precedence test
```csharp

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

```

