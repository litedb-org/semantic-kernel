// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;
using Xunit;

namespace SemanticKernel.Connectors.LiteDb.UnitTests;

public sealed class LiteDbFilterTranslatorTests
{
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
        public string[] Tags { get; set; } = Array.Empty<string>();

        [VectorStoreData]
        public string City { get; set; } = string.Empty;

        [VectorStoreData]
        public string Description { get; set; } = string.Empty;

        [VectorStoreVector(Dimensions: 3)]
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }
}
