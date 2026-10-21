using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using IronMapper.Configuration;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Fluent configuration builder for IronMapper's DI integration.
/// Passed to the <c>configure</c> delegate in
/// <see cref="IronMapperServiceCollectionExtensions.AddIronMapper(Microsoft.Extensions.DependencyInjection.IServiceCollection,Action{IronMapperOptions})"/>.
/// </summary>
public sealed class IronMapperOptions
{
    internal List<Assembly> MappingAssemblies { get; } = new();
    internal List<Action<IServiceCollection>> ServiceRegistrations { get; } = new();

    /// <summary>
    /// Registers the generated mappings of the assembly that contains <typeparamref name="TProfile"/>.
    /// The profile itself is not added to the container: profiles are read by the source generator at compile time.
    /// </summary>
    /// <typeparam name="TProfile">A concrete <see cref="MappingProfile"/> subclass with a public parameterless constructor.</typeparam>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddProfile<TProfile>() where TProfile : MappingProfile, new()
    {
        MappingAssemblies.Add(typeof(TProfile).Assembly);
        return this;
    }

    /// <summary>
    /// Registers the generated mappings of <paramref name="assembly"/> (attributes and profiles): <c>AddIronMapper</c> runs
    /// its module initializer even if nothing has used the assembly yet, and the mappings are registered when the first
    /// <c>IMapper</c> is created. The assembly is not scanned, and profiles are not added
    /// to the container: mappings come from the code generated at compile time.
    /// </summary>
    /// <param name="assembly">An assembly with generated mappings.</param>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddProfilesFromAssembly(Assembly assembly)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        MappingAssemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers <typeparamref name="TConverter"/> in the DI container as a transient service.
    /// The generated mapping code does not resolve converters from DI: it creates them with
    /// <c>new TConverter()</c>, so converters still need a public parameterless constructor.
    /// </summary>
    /// <typeparam name="TConverter">A concrete type implementing <see cref="ITypeConverter"/>.</typeparam>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConverter>()
        where TConverter : class, ITypeConverter
    {
        ServiceRegistrations.Add(static services => services.TryAddTransient<TConverter>());
        return this;
    }
}
