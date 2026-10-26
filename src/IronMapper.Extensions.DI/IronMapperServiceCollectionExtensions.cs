using System;
using System.Reflection;
using IronMapper.Interfaces;
using IronMapper.Registration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Extension methods that register IronMapper services with an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Compatible with trimming and NativeAOT: the registered <see cref="IMapper"/> calls the generated mappings through
/// the generated registry (<see cref="MapperRegistry"/>), and no assembly is scanned for types.
/// </remarks>
public static class IronMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMapper"/> and the generated mappings of the supplied assemblies.
    /// </summary>
    /// <remarks>
    /// Each assembly's module initializer is run now, so its mappings are registered when the first <see cref="IMapper"/>
    /// is created even if nothing has used the assembly by then. Pass the assemblies that hold
    /// the mappings (where the <c>[MapTo]</c>/<c>[MapFrom]</c> types or the profiles are), in particular those whose
    /// code is not used before the first mapping. Mappings of the assemblies that are already loaded are found without
    /// it. Profiles are not added to the container: they are read by the source generator at compile time.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="assemblies">
    /// Assemblies with generated mappings. Typically <c>typeof(Program).Assembly</c>.
    /// </param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIronMapper(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var options = new IronMapperOptions();
        foreach (var assembly in assemblies)
        {
            options.AddProfilesFromAssembly(assembly);
        }

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
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

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
        // Converters declared with AddConverter<T>() (registered with their static type: no Type-based registration).
        foreach (var register in options.ServiceRegistrations)
        {
            register(services);
        }

        // Run each assembly's module initializer, even if nothing used the assembly yet: it adds the assembly's
        // registration, which RuntimeMapper runs when it is created.
        foreach (var assembly in options.MappingAssemblies)
        {
            MapperRegistry.EnsureModuleInitialized(assembly);
        }

        services.TryAddSingleton<IMapper, RuntimeMapper>();

        return services;
    }
}
