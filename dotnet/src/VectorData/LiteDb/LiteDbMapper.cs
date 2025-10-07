// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;
using LiteDB;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbMapper<TRecord>(CollectionModel model)
    where TRecord : class
{
    private readonly CollectionModel _model = model;

    public BsonDocument MapToDocument(
        TRecord record,
        IReadOnlyDictionary<string, IReadOnlyList<float[]>>? generatedVectors,
        int recordIndex)
    {
        var document = new BsonDocument();

        var keyValue = this._model.KeyProperty.GetValueAsObject(record)
            ?? throw new InvalidOperationException($"Key property '{this._model.KeyProperty.ModelName}' cannot be null.");
        document[LiteDbConstants.DefaultKeyField] = new BsonValue(keyValue);

        foreach (var property in this._model.DataProperties)
        {
            var value = property.GetValueAsObject(record);
            if (value is null)
            {
                continue;
            }

            document[property.StorageName] = new BsonValue(value);
        }

        foreach (var property in this._model.VectorProperties)
        {
            float[]? vector = null;
            var value = property.GetValueAsObject(record);
            if (value is not null && TryConvertVector(value, out vector))
            {
                // vector assigned
            }
            else if (generatedVectors is not null && generatedVectors.TryGetValue(property.ModelName, out var generated))
            {
                vector = generated[recordIndex];
            }

            if (vector is not null)
            {
                document[property.StorageName] = new BsonVector(vector);
            }
        }

        return document;
    }

    public TRecord MapToRecord(BsonDocument document, bool includeVectors)
    {
        var record = this._model.CreateRecord<TRecord>();

        var keyValue = document[LiteDbConstants.DefaultKeyField];
        this._model.KeyProperty.SetValueAsObject(record, ConvertValue(keyValue, this._model.KeyProperty.Type));

        foreach (var property in this._model.DataProperties)
        {
            if (!document.TryGetValue(property.StorageName, out var bsonValue) || bsonValue.IsNull)
            {
                continue;
            }

            property.SetValueAsObject(record, ConvertValue(bsonValue, property.Type));
        }

        if (includeVectors)
        {
            foreach (var property in this._model.VectorProperties)
            {
                if (!document.TryGetValue(property.StorageName, out var bsonValue) || bsonValue.IsNull)
                {
                    continue;
                }

                var floats = bsonValue is BsonVector vector ? vector.Values : null;
                if (floats is null)
                {
                    continue;
                }

                var targetType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
                object vectorValue = targetType switch
                {
                    { } t when t == typeof(ReadOnlyMemory<float>) => new ReadOnlyMemory<float>(floats),
                    { } t when t == typeof(Embedding<float>) => new Embedding<float>(floats),
                    { } t when t == typeof(float[]) => floats,
                    _ => throw new NotSupportedException($"Vector property '{property.ModelName}' has unsupported type '{property.Type}'.")
                };

                property.SetValueAsObject(record, vectorValue);
            }
        }

        return record;
    }

    internal static bool TryConvertVector(object value, out float[]? vector)
    {
        switch (value)
        {
            case float[] floats:
                vector = floats;
                return true;
            case ReadOnlyMemory<float> memory:
                vector = memory.ToArray();
                return true;
            case Embedding<float> embedding:
                vector = embedding.Vector.ToArray();
                return true;
            default:
                vector = null;
                return false;
        }
    }

    private static object? ConvertValue(BsonValue value, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value.IsNull)
        {
            return null;
        }

        if (underlyingType == typeof(string))
        {
            return value.AsString;
        }

        if (underlyingType == typeof(int))
        {
            return value.AsInt32;
        }

        if (underlyingType == typeof(long))
        {
            return value.AsInt64;
        }

        if (underlyingType == typeof(double))
        {
            return value.AsDouble;
        }

        if (underlyingType == typeof(float))
        {
            return (float)value.AsDouble;
        }

        if (underlyingType == typeof(bool))
        {
            return value.AsBoolean;
        }

        if (underlyingType == typeof(DateTime))
        {
            return value.AsDateTime;
        }

        if (underlyingType == typeof(Guid))
        {
            return value.AsGuid;
        }

        if (underlyingType == typeof(Dictionary<string, object?>))
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var element in value.AsDocument)
            {
                dictionary[element.Key] = element.Value.RawValue;
            }

            return dictionary;
        }

        return value.RawValue;
    }
}
