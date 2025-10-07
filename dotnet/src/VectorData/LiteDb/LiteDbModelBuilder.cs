// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbModelBuilder() : CollectionModelBuilder(ValidationOptions)
{
    private const string SupportedVectorTypes = "ReadOnlyMemory<float>, float[]";

    internal static readonly CollectionModelBuildingOptions ValidationOptions = new()
    {
        RequiresAtLeastOneVector = false,
        SupportsMultipleKeys = false,
        SupportsMultipleVectors = true
    };

    protected override bool IsKeyPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = "string";
        return type == typeof(string);
    }

    protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = "";
        return true;
    }

    protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = SupportedVectorTypes;

        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(ReadOnlyMemory<float>)
            || type == typeof(float[]);
    }
}
