using System;
using System.Reflection;
using IronMapper.Configuration;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Extension methods that register IronMapper services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class IronMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers IronMapper and automatically scans the supplied assemblies for
    /// <see cref="MappingProfile"/> subclasses.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="assemblies">
    /// One or more assemblies to scan.  Typically <c>typeof(Program).Assembly</c>.
    /// </param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIronMapper(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var options = new IronMapperOptions();
        foreach (var assembly in assemblies)
            options.AddProfilesFromAssembly(assembly);

        return RegisterServices(services, options);
    }

    /// <summary>
    /// Registers IronMapper with configuration provided through <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">A delegate that populates an <see cref="IronMapperOptions"/> instance.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIronMapper(
        this IServiceCollection services,
        Action<IronMapperOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var options = new IronMapperOptions();
        configure(options);

        return RegisterServices(services, options);
    }

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private static IServiceCollection RegisterServices(
        IServiceCollection services,
        IronMapperOptions options)
    {
        // Register each explicitly declared converter type as a transient service.
        foreach (var converterType in options.ConverterTypes)
            services.TryAddTransient(converterType);

        // Register concrete MappingProfile subclasses found in each scanned assembly.
        foreach (var assembly in options.ProfileAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !type.IsClass) continue;
                if (!typeof(MappingProfile).IsAssignableFrom(type)) continue;
                services.TryAddSingleton(type);
            }
        }

        // Register the runtime mapper as the IMapper implementation.
        services.TryAddSingleton<IMapper, RuntimeMapper>();

        return services;
    }
}
