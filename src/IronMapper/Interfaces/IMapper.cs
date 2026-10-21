using System.Collections.Generic;

namespace IronMapper.Interfaces;

/// <summary>
/// Core mapper interface for mapping through an injected service instead of calling the generated
/// <c>MapTo*</c> extension methods directly. The Source Generator does not implement this interface.
/// The implementation shipped in <c>IronMapper.Extensions.DI</c> (<c>RuntimeMapper</c>) calls the generated methods
/// through the delegates of the generated registry (<see cref="Registration.MapperRegistry"/>), without reflection.
/// A call through it costs a delegate call more than the generated extension method.
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
    /// Maps each element of <paramref name="source"/> to <typeparamref name="TDest"/> and returns the results.
    /// </summary>
    /// <remarks>
    /// The source is enumerated and mapped once, during the call (the built-in implementation returns a
    /// <see cref="List{T}"/>), so enumerating the result again does not map again and exceptions are thrown by the call.
    /// </remarks>
    /// <typeparam name="TSource">The element source type.</typeparam>
    /// <typeparam name="TDest">The element destination type.</typeparam>
    /// <param name="source">The source collection to map.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> of mapped destination instances.</returns>
    IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source);
}
