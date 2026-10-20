using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// per <c>(sourceType, destType)</c> pair.  Every call then invokes the cached method via
/// <c>MethodInfo.Invoke</c>, so this mapper relies on reflection and does not work with trimming or NativeAOT
/// (the trimmer removes the generated methods; creating the mapper reports IL2026). Methods are matched by signature: a <c>MapTo*</c> method whose
/// parameter is the exact source type and whose return type is the destination type (or, for the in-place
/// overload, whose parameters are both types), so qualified method names (IM0013) are found too;
/// public and internal generated methods are both found;
/// assemblies whose names start with <c>System.</c> or
/// <c>Microsoft.</c> are not scanned.
/// </remarks>
[RequiresUnreferencedCode(TrimMessages.IMapper, Url = TrimMessages.IMapperUrl)]
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
    /// <remarks>Returns a <see cref="List{T}"/>; the elements are mapped during the call.</remarks>
    public IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var result = source is ICollection<TSource> collection ? new List<TDest>(collection.Count) : new List<TDest>();
        foreach (var item in source)
            result.Add(Map<TSource, TDest>(item));
        return result;
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
        => FindInGeneratedClass(new[] { sourceType }, destType);

    /// <summary>
    /// Scans all loaded assemblies for the two-parameter in-place extension method
    /// <c>MapToDestType(source, destination)</c>.
    /// </summary>
    private static MethodInfo? FindGeneratedInPlaceMethod(Type sourceType, Type destType)
        => FindInGeneratedClass(new[] { sourceType, destType }, typeof(void));

    private static MethodInfo? FindInGeneratedClass(Type[] paramTypes, Type returnType)
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

            // Generated methods are internal when a mapped type is internal.
            // Temporary until the reflection-free registry (plan item 1.3).
            foreach (var method in generatedClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                // By signature, not by name: the name is MapTo{Dest.Name} or a qualified name (IM0013).
                // MapToXList/MapToXArray take IEnumerable<T> and never match an exact element type.
                if (!method.Name.StartsWith("MapTo", StringComparison.Ordinal) || method.ReturnType != returnType)
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length != paramTypes.Length) continue;

                var matches = true;
                for (var i = 0; i < parameters.Length && matches; i++)
                    matches = parameters[i].ParameterType == paramTypes[i];
                if (matches) return method;
            }
        }

        return null;
    }
}
