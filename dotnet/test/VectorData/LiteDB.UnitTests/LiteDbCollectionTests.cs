// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDB;
using Xunit;

namespace SemanticKernel.Connectors.LiteDB.UnitTests;

public sealed class LiteDbCollectionTests : IAsyncLifetime, IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sk_litedb_{Guid.NewGuid():N}.db");
    private LiteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        this._database = new LiteDatabase($"Filename={this._databasePath};Connection=direct");
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        this.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        this._database?.Dispose();
        if (File.Exists(this._databasePath))
        {
            File.Delete(this._databasePath);
        }
    }

    [Fact]
    public async Task UpsertAndRetrieveRecordAsync()
    {
        using var collection = new LiteDbCollection<string, SampleRecord>(this._database, "records", new LiteDbCollectionOptions(), null);
        await collection.EnsureCollectionExistsAsync();

        var record = new SampleRecord
        {
            Id = "1",
            Category = "alpha",
            Embedding = new float[] { 1, 0, 0 }
        };

        await collection.UpsertAsync(record);
        var retrieved = await collection.GetAsync("1", new RecordRetrievalOptions { IncludeVectors = true });

        Assert.NotNull(retrieved);
        Assert.Equal("alpha", retrieved!.Category);
        Assert.Equal(new float[] { 1, 0, 0 }, retrieved.Embedding);
    }

    [Fact]
    public async Task VectorSearchReturnsNearestNeighborAsync()
    {
        using var collection = new LiteDbCollection<string, SampleRecord>(this._database, "search", new LiteDbCollectionOptions(), null);
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new SampleRecord { Id = "a", Category = "target", Embedding = new float[] { 1, 0, 0 } },
            new SampleRecord { Id = "b", Category = "other", Embedding = new float[] { 0, 1, 0 } },
        });

        VectorSearchResult<SampleRecord>? match = null;

        await foreach (var result in collection.SearchAsync(new float[] { 1, 0, 0 }, top: 1, cancellationToken: CancellationToken.None))
        {
            match = result;
            break;
        }

        Assert.NotNull(match);
        Assert.Equal("a", match!.Record.Id);
        Assert.True(match.Score > 0.9);
    }

    [Fact]
    public async Task FiltersApplyToCollectionQueriesAsync()
    {
        using var collection = new LiteDbCollection<string, SampleRecord>(this._database, "filters", new LiteDbCollectionOptions(), null);
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new SampleRecord { Id = "x", Category = "keep", Embedding = new float[] { 1, 0, 0 } },
            new SampleRecord { Id = "y", Category = "discard", Embedding = new float[] { 0, 1, 0 } },
        });

        var results = await collection.GetAsync(r => r.Category == "keep", top: 10, cancellationToken: CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal("x", results[0].Id);
    }

    private sealed class SampleRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string? Category { get; set; }

        [VectorStoreVector(3)]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
