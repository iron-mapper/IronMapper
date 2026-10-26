using System;
using System.Collections.Generic;
using IronMapper.Exceptions;
using IronMapper.Interfaces;
using IronMapper.Registration;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Runtime implementation of <see cref="IMapper"/> for use with dependency injection.
/// Calls the extension methods emitted by the IronMapper Source Generator through the delegates of the generated
/// registry (<see cref="MapperRegistry"/>, <see cref="MapperCache{TSource, TDest}"/>), without reflection: it works
/// with trimming and NativeAOT, and exceptions of the generated code (a <see langword="null"/> argument, a converter,
/// a hook) reach the caller as they are.
/// </summary>
/// <remarks>
/// <para>
/// Every assembly with generated mappings registers them through its module initializer. An assembly whose code
/// nothing has used yet (for example one that holds only profiles) is registered by <c>AddIronMapper(assembly)</c>;
/// when a mapping is not found, the loaded assemblies are registered and the lookup is repeated.
/// </para>
/// <para>
/// <c>Map&lt;TSource, TDest&gt;</c> uses the mapping of the exact pair; otherwise, like <c>Map&lt;TDest&gt;(object)</c>,
/// it looks the mapping up by type (<see cref="MapperRegistry.TryGetMap"/>): the nearest base type of the source with
/// a mapping, and collections as the destination type (<c>List&lt;T&gt;</c>, <c>T[]</c>, <c>IEnumerable&lt;T&gt;</c> and
/// the collection interfaces of <c>List&lt;T&gt;</c>) for any <c>IEnumerable&lt;S&gt;</c> source of a mapped element pair.
/// </para>
/// </remarks>
public sealed class RuntimeMapper : IMapper
{
    /// <summary>Initialises the mapper and registers the mappings of the assemblies whose module initializers have run.</summary>
    /// <param name="provider">The application service provider (kept for compatibility; mappings do not use it).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The registration of the generated mappings is turned off (<see cref="MapperRegistry.IsSupported"/>), e.g. by
    /// <c>&lt;IronMapperRegistration&gt;false&lt;/IronMapperRegistration&gt;</c>: the mapper would find no mappings.
    /// </exception>
    public RuntimeMapper(IServiceProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        MapperRegistry.EnsureSupported();
        MapperRegistry.EnsureInitialized();
    }

    /// <inheritdoc/>
    public TDest Map<TDest>(object source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        // Looked up on every call (a dictionary lookup in the registry): a cache here would keep the mapping of a base
        // type after an exact mapping is registered by an assembly loaded later.
        return (TDest)GetMap(source.GetType(), typeof(TDest))(source)!; // safe: the generated mapping returns a TDest
    }

    /// <inheritdoc/>
    public TDest Map<TSource, TDest>(TSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var map = MapperCache<TSource, TDest>.Map;
        return map is not null ? map(source) : MapSlow<TSource, TDest>(source);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Populates <paramref name="destination"/> in-place by calling the generated
    /// <c>MapToDestType(source, destination)</c> extension method.
    /// Init-only destination properties are silently skipped.
    /// </remarks>
    public void Map<TSource, TDest>(TSource source, TDest destination)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        var mapInto = MapperCache<TSource, TDest>.MapInto;
        if (mapInto is not null)
        {
            mapInto(source, destination);
            return;
        }

        GetMapInto(typeof(TSource), source.GetType(), typeof(TDest))(source, destination);
    }

    /// <inheritdoc/>
    /// <remarks>Returns a <see cref="List{T}"/>; the elements are mapped during the call.</remarks>
    public IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var result = source is ICollection<TSource> collection ? new List<TDest>(collection.Count) : new List<TDest>();
        foreach (var item in source)
        {
            result.Add(Map<TSource, TDest>(item));
        }

        return result;
    }

    /// <summary>
    /// Maps <paramref name="source"/> to <paramref name="destinationType"/>, looking the mapping up by
    /// <paramref name="sourceType"/> (see <see cref="MapperRegistry.TryGetMap"/>).
    /// </summary>
    /// <param name="source">The object to map.</param>
    /// <param name="sourceType">The source type; <paramref name="source"/> must be an instance of it.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>The mapped object.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="MappingException">No mapping is registered for the types.</exception>
    public object? Map(object source, Type sourceType, Type destinationType)
        => MapperRegistry.Map(source, sourceType, destinationType);

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private static TDest MapSlow<TSource, TDest>(TSource source)
    {
        // The registry may not have run the registrations of assemblies initialized after this mapper was created.
        MapperRegistry.EnsureInitialized();
        var map = MapperCache<TSource, TDest>.Map;
        if (map is not null)
        {
            return map(source);
        }

        var declared = typeof(TSource);
        var runtime = source!.GetType();
        if (MapperRegistry.TryGetMap(declared, typeof(TDest), out var found)
            || (runtime != declared && MapperRegistry.TryGetMap(runtime, typeof(TDest), out found)))
        {
            return (TDest)found(source)!;
        }

        return (TDest)GetMap(runtime, typeof(TDest))(source)!;
    }

    private static Func<object, object?> GetMap(Type sourceType, Type destinationType)
    {
        if (MapperRegistry.TryGetMap(sourceType, destinationType, out var map))
        {
            return map;
        }

        MapperRegistry.EnsureLoadedAssembliesRegistered();
        if (MapperRegistry.TryGetMap(sourceType, destinationType, out map))
        {
            return map;
        }

        throw MapperRegistry.NoMappingException(sourceType, destinationType);
    }

    private static Action<object, object> GetMapInto(Type declaredType, Type runtimeType, Type destinationType)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (MapperRegistry.TryGetMapInto(declaredType, destinationType, out var mapInto)
                || (runtimeType != declaredType && MapperRegistry.TryGetMapInto(runtimeType, destinationType, out mapInto)))
            {
                return mapInto;
            }

            MapperRegistry.EnsureLoadedAssembliesRegistered();
        }

        throw new MappingException(
            $"No in-place mapping registered from '{declaredType.Name}' to '{destinationType.Name}'. " +
            "Ensure a [MapTo], [MapFrom], or MappingProfile mapping exists, the " +
            "IronMapper Source Generator has run on the project that owns these types, and that project's " +
            "assembly is passed to AddIronMapper (or used before the first mapping).");
    }
}
