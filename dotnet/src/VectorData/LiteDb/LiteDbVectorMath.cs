// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Numerics.Tensors;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal static class LiteDbVectorMath
{
    public static float Compare(ReadOnlySpan<float> x, ReadOnlySpan<float> y, LiteDbDistanceMetric metric)
        => metric switch
        {
            LiteDbDistanceMetric.Cosine => TensorPrimitives.CosineSimilarity(x, y),
            LiteDbDistanceMetric.DotProduct => TensorPrimitives.Dot(x, y),
            LiteDbDistanceMetric.Euclidean => TensorPrimitives.Distance(x, y),
            _ => throw new NotSupportedException($"Unsupported distance metric '{metric}'.")
        };

    public static bool ShouldSortDescending(LiteDbDistanceMetric metric)
        => metric switch
        {
            LiteDbDistanceMetric.Euclidean => false,
            LiteDbDistanceMetric.Cosine => true,
            LiteDbDistanceMetric.DotProduct => true,
            _ => throw new NotSupportedException($"Unsupported distance metric '{metric}'.")
        };
}
