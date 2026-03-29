namespace IronMapper.Interfaces;

/// <summary>
/// Non-generic marker interface for all type converters.
/// Used as a constraint in fluent API where the exact generic parameters are not known.
/// </summary>
public interface ITypeConverter { }

/// <summary>
/// Defines a custom conversion strategy from <typeparamref name="TSource"/> to <typeparamref name="TDest"/>.
/// Implement this interface to provide non-trivial member conversion logic that cannot be expressed
/// via simple property assignments.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDest">The destination type.</typeparam>
public interface ITypeConverter<in TSource, out TDest> : ITypeConverter
{
    /// <summary>Converts a <typeparamref name="TSource"/> value to <typeparamref name="TDest"/>.</summary>
    /// <param name="source">The source value to convert.</param>
    /// <returns>The converted destination value.</returns>
    TDest Convert(TSource source);
}
