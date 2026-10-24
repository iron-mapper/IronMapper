using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// How the generated code passes a nested object to its mapping (<see cref="Models.PropertyMappingDescriptor.NullHandling"/>),
/// decided by the nullable annotations: <see langword="null"/> → <see langword="null"/> where the destination allows it,
/// a check with a clear exception where it does not, no <c>null!</c>.
/// </summary>
internal static class NullHandling
{
    /// <summary>The source is a value type: <c>MapToXCore(value)</c>, no check.</summary>
    public const string NotNull = "NotNull";

    /// <summary>
    /// The destination accepts <see langword="null"/>: <c>value is null ? null : MapToXCore(value)</c>.
    /// </summary>
    public const string Check = "Check";

    /// <summary>
    /// The destination does not accept <see langword="null"/> and the source is not annotated nullable (non-nullable, or
    /// without annotations): <c>value is null ? NestedObjectIsNull&lt;X&gt;("…") : MapToXCore(value)</c>. A
    /// <see langword="null"/> there (an EF navigation declared <c>= null!</c> and not loaded) throws
    /// <see cref="System.InvalidOperationException"/> naming both properties, not a <see cref="System.NullReferenceException"/>
    /// inside the mapping.
    /// </summary>
    public const string Throw = "Throw";

    /// <summary>
    /// How a nested object of <paramref name="source"/> is mapped to <paramref name="dest"/>; <see langword="null"/> when
    /// the source is annotated nullable and the destination is not (IM0002: no <c>null!</c>).
    /// </summary>
    public static string? ForNestedObject(ITypeSymbol source, ITypeSymbol dest)
    {
        if (source.IsValueType) return NotNull;
        if (AcceptsNull(dest)) return Check;
        return source.NullableAnnotation == NullableAnnotation.Annotated ? null : Throw;
    }

    // A reference type that is annotated nullable or has no annotation (a value type cannot take `null` here: a nested
    // object is never Nullable<T>).
    private static bool AcceptsNull(ITypeSymbol dest)
        => !dest.IsValueType && dest.NullableAnnotation != NullableAnnotation.NotAnnotated;
}
