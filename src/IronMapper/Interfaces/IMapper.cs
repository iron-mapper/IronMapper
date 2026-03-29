using System.Collections.Generic;

namespace IronMapper.Interfaces;

/// <summary>
/// Core mapper interface. Implementations are generated at compile-time by the IronMapper Source Generator.
/// The generated mapper contains zero reflection — all mappings are explicit property assignments.
/// </summary>
public interface IMapper
{
    /// <summary>
    /// Maps an <paramref name="source"/> object of a runtime type to <typeparamref name="TDest"/>.
    /// The source type is resolved at runtime; a compile-time registered mapping must exist.
    /// </summary>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to map.</param>
    /// <returns>A new instance of <typeparamref name="TDest"/> populated from <paramref name="source"/>.</returns>
    TDest Map<TDest>(object source);

    /// <summary>
    /// Maps a strongly-typed <paramref name="source"/> to a new instance of <typeparamref name="TDest"/>.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to map.</param>
    /// <returns>A new instance of <typeparamref name="TDest"/> populated from <paramref name="source"/>.</returns>
    TDest Map<TSource, TDest>(TSource source);

    /// <summary>
    /// Maps properties from <paramref name="source"/> into an existing <paramref name="destination"/> object.
    /// Only mapped properties are overwritten; unmapped properties retain their current values.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to read from.</param>
    /// <param name="destination">The existing destination object to populate.</param>
    void Map<TSource, TDest>(TSource source, TDest destination);

    /// <summary>
    /// Maps each element of <paramref name="source"/> to <typeparamref name="TDest"/>,
    /// returning the results as a lazily evaluated sequence.
    /// </summary>
    /// <typeparam name="TSource">The element source type.</typeparam>
    /// <typeparam name="TDest">The element destination type.</typeparam>
    /// <param name="source">The source collection to map.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> of mapped destination instances.</returns>
    IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source);
}
