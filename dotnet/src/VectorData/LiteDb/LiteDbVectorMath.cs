// Copyright (c) Microsoft. All rights reserved.

using System;
using LiteDB.Vector;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal static class LiteDbVectorMath
{
    internal static double Compare(ReadOnlySpan<float> x, ReadOnlySpan<float> y, string? distanceFunction)
    {
        return distanceFunction switch
        {
            null => CosineSimilarity(x, y),
            DistanceFunction.CosineSimilarity => CosineSimilarity(x, y),
            DistanceFunction.CosineDistance => CosineSimilarity(x, y),
            DistanceFunction.DotProductSimilarity => DotProduct(x, y),
            DistanceFunction.EuclideanDistance => EuclideanDistance(x, y),
            _ => throw new NotSupportedException($"The distance function '{distanceFunction}' is not supported by the LiteDB connector.")
        };
    }

    internal static double ConvertScore(double score, string? distanceFunction)
    {
        return distanceFunction switch
        {
            DistanceFunction.CosineDistance => 1 - score,
            _ => score
        };
    }

    internal static bool ShouldSortDescending(string? distanceFunction)
    {
        return distanceFunction switch
        {
            null => true,
            DistanceFunction.CosineSimilarity => true,
            DistanceFunction.DotProductSimilarity => true,
            DistanceFunction.CosineDistance => false,
            DistanceFunction.EuclideanDistance => false,
            _ => throw new NotSupportedException($"The distance function '{distanceFunction}' is not supported by the LiteDB connector.")
        };
    }

    internal static VectorDistanceMetric ToLiteDbMetric(string? distanceFunction, VectorDistanceMetric defaultMetric)
    {
        return distanceFunction switch
        {
            null => defaultMetric,
            DistanceFunction.CosineSimilarity => VectorDistanceMetric.Cosine,
            DistanceFunction.CosineDistance => VectorDistanceMetric.Cosine,
            DistanceFunction.DotProductSimilarity => VectorDistanceMetric.DotProduct,
            DistanceFunction.EuclideanDistance => VectorDistanceMetric.Euclidean,
            _ => defaultMetric
        };
    }

    private static double CosineSimilarity(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        double dot = 0;
        double normX = 0;
        double normY = 0;

        for (int i = 0; i < x.Length && i < y.Length; i++)
        {
            var xv = x[i];
            var yv = y[i];
            dot += xv * yv;
            normX += xv * xv;
            normY += yv * yv;
        }

        if (normX == 0 || normY == 0)
        {
            return 0;
        }

        return dot / Math.Sqrt(normX * normY);
    }

    private static double DotProduct(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        double dot = 0;
        for (int i = 0; i < x.Length && i < y.Length; i++)
        {
            dot += x[i] * y[i];
        }

        return dot;
    }

    private static double EuclideanDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        double sum = 0;
        for (int i = 0; i < x.Length && i < y.Length; i++)
        {
            var diff = x[i] - y[i];
            sum += diff * diff;
        }

        return Math.Sqrt(sum);
    }
}
