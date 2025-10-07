// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbModelBuilder() : CollectionModelBuilder(ValidationOptions)
{
    private const string SupportedKeyTypes = "string";
    internal const string SupportedVectorTypes = "ReadOnlyMemory<float>, Embedding<float>, float[]";

    internal static readonly CollectionModelBuildingOptions ValidationOptions = new()
    {
        SupportsMultipleKeys = false,
        SupportsMultipleVectors = true,
        RequiresAtLeastOneVector = false
    };

    protected override bool IsKeyPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = SupportedKeyTypes;
        return type == typeof(string);
    }

    protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = string.Empty;
        return true;
    }

    protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = SupportedVectorTypes;
        return type == typeof(ReadOnlyMemory<float>)
            || type == typeof(ReadOnlyMemory<float>?)
            || type == typeof(Embedding<float>)
            || type == typeof(float[]);
    }
}
