using System;
using System.Threading;

namespace IronMapper.Registration;

/// <summary>
/// The generated mapping delegates of one type pair, for code that maps by type parameters without reflection
/// (the <c>IMapper</c> of <c>IronMapper.Extensions.DI</c>). Filled by <see cref="MapperRegistry"/> from the
/// registration the source generator emits for every mapping of an assembly.
/// </summary>
/// <remarks>
/// The delegates call the generated <c>MapTo*</c> methods directly. They are <see langword="null"/> until the
/// registry is initialized (<see cref="MapperRegistry.EnsureInitialized"/>; creating the <c>IMapper</c> does it) and
/// for a pair without a generated mapping. Only exact types are stored here; base types and collections are resolved
/// by <see cref="MapperRegistry.TryGetMap"/>.
/// </remarks>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDest">The destination type.</typeparam>
public static class MapperCache<TSource, TDest>
{
    private static Func<TSource, TDest>? s_map;
    private static Action<TSource, TDest>? s_mapInto;

    /// <summary>Maps to a new <typeparamref name="TDest"/> (the generated <c>MapToX(source)</c>).</summary>
    public static Func<TSource, TDest>? Map => s_map;

    /// <summary>Maps into an existing <typeparamref name="TDest"/> (the generated <c>MapToX(source, destination)</c>).</summary>
    public static Action<TSource, TDest>? MapInto => s_mapInto;

    /// <summary>Sets the delegates once; a later registration of the same pair does not replace them.</summary>
    internal static void Set(Func<TSource, TDest> map, Action<TSource, TDest> mapInto)
    {
        Interlocked.CompareExchange(ref s_mapInto, mapInto, null);
        Interlocked.CompareExchange(ref s_map, map, null);
    }
}
