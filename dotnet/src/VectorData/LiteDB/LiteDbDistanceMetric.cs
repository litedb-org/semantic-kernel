// Copyright (c) Microsoft. All rights reserved.

using System;
using LiteDB.Vector;
using Microsoft.Extensions.VectorData;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

internal static class LiteDbDistanceMetric
{
    public static VectorDistanceMetric ToLiteDbMetric(string? distanceFunction)
    {
        return Normalize(distanceFunction) switch
        {
            DistanceFunction.CosineSimilarity or DistanceFunction.CosineDistance => VectorDistanceMetric.Cosine,
            DistanceFunction.DotProductSimilarity => VectorDistanceMetric.DotProduct,
            DistanceFunction.EuclideanDistance => VectorDistanceMetric.Euclidean,
            _ => VectorDistanceMetric.Cosine,
        };
    }

    public static double ComputeSimilarity(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate, string? distanceFunction)
    {
        var normalized = Normalize(distanceFunction);

        return normalized switch
        {
            DistanceFunction.CosineSimilarity or DistanceFunction.CosineDistance => CosineSimilarity(query, candidate),
            DistanceFunction.DotProductSimilarity => DotProduct(query, candidate),
            DistanceFunction.EuclideanDistance => 1.0 / (1.0 + EuclideanDistance(query, candidate)),
            _ => CosineSimilarity(query, candidate),
        };
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? DistanceFunction.CosineSimilarity : value;

    private static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (int i = 0; i < left.Length; i++)
        {
            var l = left[i];
            var r = right[i];
            dot += l * r;
            leftNorm += l * l;
            rightNorm += r * r;
        }

        var denominator = Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm);
        if (denominator == 0)
        {
            return 0;
        }

        return dot / denominator;
    }

    private static double DotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double result = 0;
        for (int i = 0; i < left.Length; i++)
        {
            result += left[i] * right[i];
        }

        return result;
    }

    private static double EuclideanDistance(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double sum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            var diff = left[i] - right[i];
            sum += diff * diff;
        }

        return Math.Sqrt(sum);
    }
}
