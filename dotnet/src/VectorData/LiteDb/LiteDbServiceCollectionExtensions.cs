// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.LiteDb;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register LiteDB <see cref="VectorStore"/> instances on an <see cref="IServiceCollection"/>.
/// </summary>
public static class LiteDbServiceCollectionExtensions
{
    private const string DynamicCodeMessage = "This method is incompatible with NativeAOT, consult the documentation for adding collections in a way that's compatible with NativeAOT.";
    private const string UnreferencedCodeMessage = "This method is incompatible with trimming, consult the documentation for adding collections in a way that's compatible with NativeAOT.";

    /// <summary>
    /// Registers a <see cref="LiteDbVectorStore"/> as <see cref="VectorStore"/> using the specified connection string.
    /// </summary>
    /// <inheritdoc cref="AddKeyedLiteDbVectorStore(IServiceCollection, object?, string, LiteDbVectorStoreOptions?, ServiceLifetime)"/>
    public static IServiceCollection AddLiteDbVectorStore(
        this IServiceCollection services,
        string connectionString,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedLiteDbVectorStore(services, serviceKey: null, connectionString, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="LiteDbVectorStore"/> as <see cref="VectorStore"/> using the specified connection string.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the store on.</param>
    /// <param name="serviceKey">The key with which to associate the store.</param>
    /// <param name="connectionString">The LiteDB connection string.</param>
    /// <param name="options">Additional options to configure the store.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddKeyedLiteDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string connectionString,
        LiteDbVectorStoreOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        if (options is not null && (options.Database is not null || options.DatabaseFactory is not null))
        {
            throw new ArgumentException("When providing a connection string do not supply Database or DatabaseFactory within the options.", nameof(options));
        }

        services.Add(new ServiceDescriptor(typeof(LiteDbVectorStore), serviceKey, (serviceProvider, _) =>
        {
            var storeOptions = PrepareStoreOptions(serviceProvider, options, connectionString);
            return new LiteDbVectorStore(storeOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<LiteDbVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="LiteDbVectorStore"/> using an options factory.
    /// </summary>
    /// <inheritdoc cref="AddKeyedLiteDbVectorStore(IServiceCollection, object?, Func{IServiceProvider, LiteDbVectorStoreOptions?}, ServiceLifetime)"/>
    public static IServiceCollection AddLiteDbVectorStore(
        this IServiceCollection services,
        Func<IServiceProvider, LiteDbVectorStoreOptions?>? optionsProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedLiteDbVectorStore(services, serviceKey: null, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="LiteDbVectorStore"/> using an options factory.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the store on.</param>
    /// <param name="serviceKey">The key with which to associate the store.</param>
    /// <param name="optionsProvider">Factory that produces the <see cref="LiteDbVectorStoreOptions"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddKeyedLiteDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, LiteDbVectorStoreOptions?>? optionsProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(new ServiceDescriptor(typeof(LiteDbVectorStore), serviceKey, (serviceProvider, _) =>
        {
            var storeOptions = GetStoreOptions(serviceProvider, optionsProvider);
            return new LiteDbVectorStore(storeOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<LiteDbVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="VectorStoreCollection{TKey, TRecord}"/> backed by LiteDB using a previously registered store.
    /// </summary>
    /// <inheritdoc cref="AddKeyedLiteDbCollection{TKey, TRecord}(IServiceCollection, object?, string, Func{IServiceProvider, VectorStoreCollectionDefinition?>?, ServiceLifetime)"/>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddLiteDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, VectorStoreCollectionDefinition?>? definitionProvider = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedLiteDbCollection<TKey, TRecord>(services, serviceKey: null, name, definitionProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="VectorStoreCollection{TKey, TRecord}"/> backed by LiteDB using a previously registered store.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the collection on.</param>
    /// <param name="serviceKey">The key with which to associate the collection (and matching store).</param>
    /// <param name="name">The logical name of the collection.</param>
    /// <param name="definitionProvider">Optional provider supplying the <see cref="VectorStoreCollectionDefinition"/>.</param>
    /// <param name="lifetime">The service lifetime for the collection. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedLiteDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, VectorStoreCollectionDefinition?>? definitionProvider = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
        }

        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), serviceKey, (serviceProvider, key) =>
        {
            var store = serviceProvider.GetRequiredKeyedService<LiteDbVectorStore>(key);
            var definition = definitionProvider?.Invoke(serviceProvider);
            return store.GetCollection<TKey, TRecord>(name, definition);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<VectorStoreCollection<TKey, TRecord>>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a dynamic <see cref="VectorStoreCollection{TKey, TRecord}"/> backed by LiteDB using a previously registered store.
    /// </summary>
    /// <inheritdoc cref="AddKeyedLiteDbDynamicCollection(IServiceCollection, object?, string, Func{IServiceProvider, VectorStoreCollectionDefinition}, ServiceLifetime)"/>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddLiteDbDynamicCollection(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, VectorStoreCollectionDefinition> definitionProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedLiteDbDynamicCollection(services, serviceKey: null, name, definitionProvider, lifetime);

    /// <summary>
    /// Registers a keyed dynamic <see cref="VectorStoreCollection{TKey, TRecord}"/> backed by LiteDB using a previously registered store.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the collection on.</param>
    /// <param name="serviceKey">The key with which to associate the collection (and matching store).</param>
    /// <param name="name">The logical name of the collection.</param>
    /// <param name="definitionProvider">Provider supplying the <see cref="VectorStoreCollectionDefinition"/>.</param>
    /// <param name="lifetime">The service lifetime for the collection. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedLiteDbDynamicCollection(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, VectorStoreCollectionDefinition> definitionProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(definitionProvider);

        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<object, Dictionary<string, object?>>), serviceKey, (serviceProvider, key) =>
        {
            var store = serviceProvider.GetRequiredKeyedService<LiteDbVectorStore>(key);
            var definition = definitionProvider(serviceProvider);
            return store.GetDynamicCollection(name, definition);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<Dictionary<string, object?>>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<VectorStoreCollection<object, Dictionary<string, object?>>>(key), lifetime));

        return services;
    }

    private static LiteDbVectorStoreOptions PrepareStoreOptions(IServiceProvider serviceProvider, LiteDbVectorStoreOptions? options, string connectionString)
    {
        var storeOptions = new LiteDbVectorStoreOptions(options)
        {
            ConnectionString = connectionString
        };

        ApplyEmbeddingGeneratorIfMissing(serviceProvider, storeOptions);
        return storeOptions;
    }

    private static LiteDbVectorStoreOptions GetStoreOptions(IServiceProvider serviceProvider, Func<IServiceProvider, LiteDbVectorStoreOptions?>? optionsProvider)
    {
        var provided = optionsProvider?.Invoke(serviceProvider);
        var storeOptions = new LiteDbVectorStoreOptions(provided);

        if (string.IsNullOrWhiteSpace(storeOptions.ConnectionString) &&
            storeOptions.Database is null &&
            storeOptions.DatabaseFactory is null)
        {
            storeOptions.ConnectionString = LiteDbConstants.DefaultConnectionString;
        }

        ApplyEmbeddingGeneratorIfMissing(serviceProvider, storeOptions);
        return storeOptions;
    }

    private static void ApplyEmbeddingGeneratorIfMissing(IServiceProvider serviceProvider, LiteDbVectorStoreOptions storeOptions)
    {
        if (storeOptions.EmbeddingGenerator is null)
        {
            var embeddingGenerator = serviceProvider.GetService<IEmbeddingGenerator>();
            if (embeddingGenerator is not null)
            {
                storeOptions.EmbeddingGenerator = embeddingGenerator;
            }
        }
    }
}
