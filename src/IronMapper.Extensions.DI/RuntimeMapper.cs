using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using IronMapper.Exceptions;
using IronMapper.Interfaces;

namespace IronMapper.Extensions.DI;

/// <summary>
/// Runtime implementation of <see cref="IMapper"/> for use with dependency injection.
/// Delegates all mapping calls to the extension methods emitted by the IronMapper Source Generator
/// (found in <c>IronMapper.Generated.GeneratedMappers</c>).
/// </summary>
/// <remarks>
/// On the first mapping call the mapper scans every loaded assembly for the generated
/// <c>IronMapper.Generated.GeneratedMappers</c> static class and caches a <see cref="MethodInfo"/>
/// per <c>(sourceType, destType)</c> pair.  Subsequent calls pay only a dictionary lookup.
/// </remarks>
public sealed class RuntimeMapper : IMapper
{
    // null value = mapping was looked up and not found (negative cache).
    private readonly ConcurrentDictionary<(Type Source, Type Dest), MethodInfo?> _cache = new();
    private readonly ConcurrentDictionary<(Type Source, Type Dest), MethodInfo?> _inPlaceCache = new();
    private readonly IServiceProvider _provider;

    /// <summary>Initialises the mapper with the application's <see cref="IServiceProvider"/>.</summary>
    /// <param name="provider">The application service provider used for dependency resolution.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <see langword="null"/>.</exception>
    public RuntimeMapper(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc/>
    public TDest Map<TDest>(object source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return (TDest)MapInternal(source.GetType(), typeof(TDest), source)!; // safe: MapInternal throws MappingException rather than returning null
    }

    /// <inheritdoc/>
    public TDest Map<TSource, TDest>(TSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return (TDest)MapInternal(typeof(TSource), typeof(TDest), source)!; // safe: MapInternal throws MappingException rather than returning null
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Populates <paramref name="destination"/> in-place by calling the generated
    /// <c>MapToDestType(source, destination)</c> extension method.
    /// Init-only destination properties are silently skipped.
    /// </remarks>
    public void Map<TSource, TDest>(TSource source, TDest destination)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        var key = (typeof(TSource), typeof(TDest));
        if (!_inPlaceCache.TryGetValue(key, out var method))
        {
            method = FindGeneratedInPlaceMethod(typeof(TSource), typeof(TDest));
            _inPlaceCache.TryAdd(key, method);
        }

        if (method is null)
        {
            throw new MappingException(
                $"No in-place mapping registered from '{typeof(TSource).Name}' to '{typeof(TDest).Name}'. " +
                "Ensure a [MapTo], [MapFrom], or MappingProfile mapping exists and the " +
                "IronMapper Source Generator has run on the project that owns these types.");
        }

        method.Invoke(null, new object[] { source, destination });
    }

    /// <inheritdoc/>
    public IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        foreach (var item in source)
            yield return Map<TSource, TDest>(item);
    }

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private object? MapInternal(Type sourceType, Type destType, object source)
    {
        var key = (sourceType, destType);

        if (!_cache.TryGetValue(key, out var method))
        {
            method = FindGeneratedMethod(sourceType, destType);
            _cache.TryAdd(key, method);
        }

        if (method is null)
        {
            throw new MappingException(
                $"No mapping registered from '{sourceType.Name}' to '{destType.Name}'. " +
                "Ensure a [MapTo], [MapFrom], or MappingProfile mapping exists and the " +
                "IronMapper Source Generator has run on the project that owns these types.");
        }

        return method.Invoke(null, new[] { source });
    }

    /// <summary>
    /// Scans all loaded assemblies for the generated <c>IronMapper.Generated.GeneratedMappers</c>
    /// static class and returns the single-parameter extension method matching the requested type pair.
    /// </summary>
    private static MethodInfo? FindGeneratedMethod(Type sourceType, Type destType)
    {
        var expectedName = $"MapTo{destType.Name}";
        return FindInGeneratedClass(expectedName, new[] { sourceType });
    }

    /// <summary>
    /// Scans all loaded assemblies for the two-parameter in-place extension method
    /// <c>MapToDestType(source, destination)</c>.
    /// </summary>
    private static MethodInfo? FindGeneratedInPlaceMethod(Type sourceType, Type destType)
    {
        var expectedName = $"MapTo{destType.Name}";
        return FindInGeneratedClass(expectedName, new[] { sourceType, destType });
    }

    private static MethodInfo? FindInGeneratedClass(string methodName, Type[] paramTypes)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Fast path: skip known framework / system assemblies.
            var asmName = assembly.GetName().Name;
            if (asmName is null) continue;
            if (asmName.StartsWith("System.", StringComparison.Ordinal)
                || asmName.StartsWith("Microsoft.", StringComparison.Ordinal)
                || asmName == "mscorlib" || asmName == "netstandard")
                continue;

            var generatedClass = assembly.GetType("IronMapper.Generated.GeneratedMappers");
            if (generatedClass is null) continue;

            var method = generatedClass.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                paramTypes,
                null);

            if (method is not null) return method;
        }

        return null;
    }
}
