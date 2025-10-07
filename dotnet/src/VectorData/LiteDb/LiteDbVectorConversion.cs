// Copyright (c) Microsoft. All rights reserved.

using System;
using LiteDB;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal static class LiteDbVectorConversion
{
    internal static float[] GetVectorFromRecord<TRecord>(TRecord record, VectorPropertyModel vectorProperty)
        where TRecord : class
    {
        var value = vectorProperty.GetValueAsObject(record)
            ?? throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' must be populated before upserting records.");

        return value switch
        {
            ReadOnlyMemory<float> memory => memory.ToArray(),
            float[] array => array,
            _ => throw new NotSupportedException($"Vector property '{vectorProperty.ModelName}' is of unsupported type '{value.GetType().Name}'.")
        };
    }

    internal static object ConvertVectorToProperty(BsonValue value, VectorPropertyModel vectorProperty)
    {
        if (!value.IsVector)
        {
            throw new InvalidOperationException($"Field '{vectorProperty.StorageName}' does not contain a LiteDB vector.");
        }

        var vector = (float[])value.RawValue;

        return vectorProperty.Type == typeof(ReadOnlyMemory<float>) || vectorProperty.Type == typeof(ReadOnlyMemory<float>?)
            ? new ReadOnlyMemory<float>(vector)
            : vector;
    }
}
