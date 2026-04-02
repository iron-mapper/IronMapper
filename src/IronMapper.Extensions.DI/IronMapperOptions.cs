using System;
using System.Collections.Generic;
using System.Reflection;
using IronMapper.Configuration;
using IronMapper.Interfaces;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Fluent configuration builder for IronMapper's DI integration.
/// Passed to the <c>configure</c> delegate in
/// <see cref="IronMapperServiceCollectionExtensions.AddIronMapper(Microsoft.Extensions.DependencyInjection.IServiceCollection,Action{IronMapperOptions})"/>.
/// </summary>
public sealed class IronMapperOptions
{
    internal List<Assembly> ProfileAssemblies { get; } = new();
    internal List<Type> ConverterTypes { get; } = new();

    /// <summary>
    /// Registers the assembly containing <typeparamref name="TProfile"/> for profile scanning.
    /// The profile type itself must have a public parameterless constructor.
    /// </summary>
    /// <typeparam name="TProfile">A concrete <see cref="MappingProfile"/> subclass with a public parameterless constructor.</typeparam>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddProfile<TProfile>() where TProfile : MappingProfile, new()
    {
        ProfileAssemblies.Add(typeof(TProfile).Assembly);
        return this;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for all concrete <see cref="MappingProfile"/> subclasses
    /// and registers their assemblies so the mapper is aware of any mappings they declare.
    /// </summary>
    /// <param name="assembly">The assembly to scan for <see cref="MappingProfile"/> subclasses.</param>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddProfilesFromAssembly(Assembly assembly)
    {
        ProfileAssemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers <typeparamref name="TConverter"/> in the DI container as a transient service
    /// so it can be resolved alongside the generated mapping code.
    /// </summary>
    /// <typeparam name="TConverter">A concrete type implementing <see cref="ITypeConverter"/>.</typeparam>
    /// <returns>This <see cref="IronMapperOptions"/> instance for method chaining.</returns>
    public IronMapperOptions AddConverter<TConverter>() where TConverter : class, ITypeConverter
    {
        ConverterTypes.Add(typeof(TConverter));
        return this;
    }
}
