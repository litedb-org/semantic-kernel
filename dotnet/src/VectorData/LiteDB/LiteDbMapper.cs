// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

internal sealed class LiteDbMapper<TRecord>
    where TRecord : class
{
    private readonly CollectionModel _model;
    private readonly BsonMapper _mapper;

    public LiteDbMapper(CollectionModel model, BsonMapper? mapper = null)
    {
        this._model = model;
        this._mapper = mapper ?? BsonMapper.Global;
    }

    public BsonDocument MapFromDataToStorageModel(TRecord record, int recordIndex, IReadOnlyList<Embedding>?[]? generatedEmbeddings)
    {
        var document = new BsonDocument();

        var keyValue = this._model.KeyProperty.GetValueAsObject(record) ?? throw new InvalidOperationException("LiteDB vector records must have a non-null key value.");
        if (keyValue is not string key)
        {
            throw new InvalidOperationException("LiteDB vector records must use string keys.");
        }

        document[LiteDbConstants.ReservedKeyFieldName] = new BsonValue(key);

        foreach (var property in this._model.DataProperties)
        {
            var value = property.GetValueAsObject(record);
            document[property.StorageName] = value is null ? BsonValue.Null : this._mapper.Serialize(property.Type, value);
        }

        if (this._model.VectorProperties.Count == 0)
        {
            return document;
        }

        for (var i = 0; i < this._model.VectorProperties.Count; i++)
        {
            var property = this._model.VectorProperties[i];

            Embedding<float>? embedding = generatedEmbeddings?[i] is IReadOnlyList<Embedding> embeddings
                ? embeddings[recordIndex] as Embedding<float>
                : null;

            float[] vector = embedding is not null
                ? embedding.Vector.ToArray()
                : this.MaterializeVector(property, record);

            document[property.StorageName] = new BsonVector(vector);
        }

        return document;
    }

    public TRecord MapFromStorageToDataModel(BsonDocument document, bool includeVectors)
    {
        var record = this._model.CreateRecord<TRecord>();

        if (!document.TryGetValue(LiteDbConstants.ReservedKeyFieldName, out var keyValue))
        {
            throw new InvalidOperationException("LiteDB document is missing the _id field.");
        }

        this._model.KeyProperty.SetValueAsObject(record, this._mapper.Deserialize(typeof(string), keyValue));

        foreach (var property in this._model.DataProperties)
        {
            if (!document.TryGetValue(property.StorageName, out var value) || value.IsNull)
            {
                continue;
            }

            var deserialized = this._mapper.Deserialize(property.Type, value);
            property.SetValueAsObject(record, deserialized);
        }

        if (includeVectors)
        {
            for (var i = 0; i < this._model.VectorProperties.Count; i++)
            {
                var property = this._model.VectorProperties[i];
                if (!document.TryGetValue(property.StorageName, out var value) || value.IsNull)
                {
                    continue;
                }

                if (value is not BsonVector bsonVector)
                {
                    throw new InvalidOperationException($"LiteDB vector property '{property.StorageName}' was not stored as a vector value.");
                }

                var floats = bsonVector.Values;
                var targetType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
                object? typedValue = targetType switch
                {
                    var t when t == typeof(ReadOnlyMemory<float>) => new ReadOnlyMemory<float>(floats),
                    var t when t == typeof(Embedding<float>) => new Embedding<float>(floats),
                    var t when t == typeof(float[]) => floats.ToArray(),
                    _ => throw new InvalidOperationException($"Vector property '{property.ModelName}' is configured with unsupported type '{property.Type}'.")
                };

                property.SetValueAsObject(record, typedValue);
            }
        }

        return record;
    }

    private float[] MaterializeVector(VectorPropertyModel property, TRecord record)
    {
        var rawValue = property.GetValueAsObject(record);
        if (rawValue is null)
        {
            throw new InvalidOperationException($"Vector property '{property.ModelName}' does not have a value and no embedding generator was configured.");
        }

        var targetType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        return targetType switch
        {
            var t when t == typeof(ReadOnlyMemory<float>) => ((ReadOnlyMemory<float>)rawValue).ToArray(),
            var t when t == typeof(Embedding<float>) => ((Embedding<float>)rawValue).Vector.ToArray(),
            var t when t == typeof(float[]) => ((float[])rawValue).ToArray(),
            _ => throw new InvalidOperationException($"Vector property '{property.ModelName}' is configured with unsupported type '{property.Type}'.")
        };
    }
}
