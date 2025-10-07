// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

internal sealed class LiteDbModelBuilder() : CollectionModelBuilder(s_options)
{
    private static readonly CollectionModelBuildingOptions s_options = new()
    {
        RequiresAtLeastOneVector = true,
        SupportsMultipleKeys = false,
        SupportsMultipleVectors = false,
        ReservedKeyStorageName = LiteDbConstants.ReservedKeyFieldName,
        UsesExternalSerializer = true,
    };

    internal const string SupportedVectorTypes = "ReadOnlyMemory<float>, Embedding<float>, float[]";

    protected override bool IsKeyPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = "string";
        return type == typeof(string);
    }

    protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = "bool, string, numeric types, DateTime, DateTimeOffset, Guid, byte[], or arrays/lists of these";

        type = Nullable.GetUnderlyingType(type) ?? type;

        return IsSupported(type)
            || (type.IsArray && IsSupported(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>) && IsSupported(type.GenericTypeArguments[0]));

        static bool IsSupported(Type candidate)
            => candidate == typeof(bool)
            || candidate == typeof(string)
            || candidate == typeof(int)
            || candidate == typeof(long)
            || candidate == typeof(short)
            || candidate == typeof(double)
            || candidate == typeof(float)
            || candidate == typeof(decimal)
            || candidate == typeof(Guid)
            || candidate == typeof(DateTime)
            || candidate == typeof(DateTimeOffset)
            || candidate == typeof(byte[]);
    }

    protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
        => IsVectorPropertyTypeValidCore(type, out supportedTypes);

    internal static bool IsVectorPropertyTypeValidCore(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = SupportedVectorTypes;

        return type == typeof(ReadOnlyMemory<float>)
            || type == typeof(ReadOnlyMemory<float>?)
            || type == typeof(Embedding<float>)
            || type == typeof(float[]);
    }
}
