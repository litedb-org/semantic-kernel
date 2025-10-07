// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using LiteDB;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbRecordMapper<TRecord>
    where TRecord : class
{
    private readonly CollectionModel _model;

    public LiteDbRecordMapper(CollectionModel model)
    {
        this._model = model;
    }

    public BsonDocument ToDocument(TRecord record)
    {
        var document = new BsonDocument();
        var key = this._model.KeyProperty.GetValueAsObject(record)
            ?? throw new InvalidOperationException("Vector store records must define a non-null key.");

        document[LiteDbWellKnownFields.Id] = new BsonValue(key);

        foreach (var dataProperty in this._model.DataProperties)
        {
            var value = dataProperty.GetValueAsObject(record);

            if (value is null)
            {
                continue;
            }

            document[dataProperty.StorageName] = BsonMapper.Global.Serialize(dataProperty.Type, value);
        }

        foreach (var vectorProperty in this._model.VectorProperties)
        {
            var vector = LiteDbVectorConversion.GetVectorFromRecord(record, vectorProperty);
            document[vectorProperty.StorageName] = new BsonVector(vector);
        }

        return document;
    }

    public TRecord ToRecord(BsonDocument document, bool includeVectors)
    {
        var record = this._model.CreateRecord<TRecord>();

        if (document.TryGetValue(LiteDbWellKnownFields.Id, out var keyValue))
        {
            this._model.KeyProperty.SetValueAsObject(record, this.ConvertValue(keyValue, this._model.KeyProperty.Type));
        }

        foreach (var dataProperty in this._model.DataProperties)
        {
            if (!document.TryGetValue(dataProperty.StorageName, out var value) || value.IsNull)
            {
                continue;
            }

            var converted = this.ConvertValue(value, dataProperty.Type);
            dataProperty.SetValueAsObject(record, converted);
        }

        if (includeVectors)
        {
            foreach (var vectorProperty in this._model.VectorProperties)
            {
                if (!document.TryGetValue(vectorProperty.StorageName, out var value) || value.IsNull)
                {
                    continue;
                }

                var vector = LiteDbVectorConversion.ConvertVectorToProperty(value, vectorProperty);
                vectorProperty.SetValueAsObject(record, vector);
            }
        }

        return record;
    }

    private object? ConvertValue(BsonValue value, Type targetType)
    {
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.IsNull)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        if (targetType == typeof(string))
        {
            return value.AsString;
        }

        if (targetType == typeof(int))
        {
            return value.AsInt32;
        }

        if (targetType == typeof(long))
        {
            return value.AsInt64;
        }

        if (targetType == typeof(double))
        {
            return value.AsDouble;
        }

        if (targetType == typeof(float))
        {
            return (float)value.AsDouble;
        }

        if (targetType == typeof(decimal))
        {
            return value.AsDecimal;
        }

        if (targetType == typeof(bool))
        {
            return value.AsBoolean;
        }

        if (targetType == typeof(DateTime))
        {
            return value.AsDateTime;
        }

        if (targetType == typeof(Guid))
        {
            return value.AsGuid;
        }

        if (targetType == typeof(Dictionary<string, object?>))
        {
            var dictionary = new Dictionary<string, object?>();

            foreach (var kvp in value.AsDocument)
            {
                dictionary[kvp.Key] = this.ConvertValue(kvp.Value, typeof(object));
            }

            return dictionary;
        }

        return BsonMapper.Global.Deserialize(targetType, value);
    }
}
