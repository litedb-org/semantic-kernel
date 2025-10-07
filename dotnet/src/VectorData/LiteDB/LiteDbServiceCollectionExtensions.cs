// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using LiteDB;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.LiteDB;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering LiteDB-backed vector stores with <see cref="IServiceCollection"/>.
/// </summary>
public static class LiteDbServiceCollectionExtensions
{
    private const string DynamicCodeMessage = "This method is incompatible with NativeAOT, consult the documentation for adding collections in a way that's compatible with NativeAOT.";
    private const string UnreferencedCodeMessage = "This method is incompatible with trimming, consult the documentation for adding collections in a way that's compatible with NativeAOT.";

    /// <summary>
    /// Registers a <see cref="LiteDbVectorStore"/> that uses an externally managed <see cref="LiteDatabase"/> instance.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="options">Optional store configuration.</param>
    /// <param name="lifetime">Service lifetime for the registered store.</param>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddLiteDbVectorStore(
        this IServiceCollection services,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedLiteDbVectorStore(services, serviceKey: null, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="LiteDbVectorStore"/> that uses an externally managed <see cref="LiteDatabase"/> instance.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="serviceKey">Key used when resolving the store.</param>
    /// <param name="options">Optional store configuration.</param>
    /// <param name="lifetime">Service lifetime for the registered store.</param>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedLiteDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Verify.NotNull(services);

        services.Add(new ServiceDescriptor(typeof(LiteDbVectorStore), serviceKey, (sp, _) =>
        {
            var database = sp.GetRequiredService<LiteDatabase>();
            var resolvedOptions = GetStoreOptions(sp, _ => options);
            return new LiteDbVectorStore(database, resolvedOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey, static (sp, key) => sp.GetRequiredKeyedService<LiteDbVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="LiteDbVectorStore"/> that manages its own <see cref="LiteDatabase"/> connection using the provided connection string.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">LiteDB connection string.</param>
    /// <param name="options">Optional store configuration.</param>
    /// <param name="lifetime">Service lifetime for the registered store.</param>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddLiteDbVectorStore(
        this IServiceCollection services,
        string connectionString,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedLiteDbVectorStore(services, serviceKey: null, connectionString, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="LiteDbVectorStore"/> that manages its own <see cref="LiteDatabase"/> connection using the provided connection string.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="serviceKey">Key used when resolving the store.</param>
    /// <param name="connectionString">LiteDB connection string.</param>
    /// <param name="options">Optional store configuration.</param>
    /// <param name="lifetime">Service lifetime for the registered store.</param>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedLiteDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string connectionString,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Verify.NotNull(services);
        Verify.NotNullOrWhiteSpace(connectionString);

        services.Add(new ServiceDescriptor(typeof(LiteDbVectorStore), serviceKey, (sp, _) =>
        {
            var resolvedOptions = GetStoreOptions(sp, _ => options);
            return new LiteDbVectorStore(connectionString, resolvedOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey, static (sp, key) => sp.GetRequiredKeyedService<LiteDbVectorStore>(key), lifetime));

        return services;
    }

    private static LiteDbVectorStoreOptions GetStoreOptions(IServiceProvider serviceProvider, Func<IServiceProvider, LiteDbVectorStoreOptions?> factory)
    {
        var options = factory(serviceProvider);
        if (options is not null)
        {
            return options;
        }

        return serviceProvider.GetService<LiteDbVectorStoreOptions>() ?? new LiteDbVectorStoreOptions();
    }
}
