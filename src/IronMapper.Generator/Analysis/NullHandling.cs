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
    /// A value converted by the mapping of the underlying types of a <c>Nullable&lt;T&gt;</c> source (<c>Guid?</c> by the
    /// mapping of <c>Guid</c>): <c>value.HasValue ? MapToXCore(value.Value) : null</c>; the destination accepts
    /// <see langword="null"/>.
    /// </summary>
    public const string Lifted = "Lifted";

    /// <summary>
    /// How a nested object of <paramref name="source"/> is mapped to <paramref name="dest"/>; <see langword="null"/> when
    /// the source is annotated nullable and the destination is not (IM0002: no <c>null!</c>).
    /// </summary>
    public static string? ForNestedObject(ITypeSymbol source, ITypeSymbol dest)
    {
        if (source.IsValueType)
        {
            return NotNull;
        }

        if (AcceptsNull(dest))
        {
            return Check;
        }

        return source.NullableAnnotation == NullableAnnotation.Annotated ? null : Throw;
    }

    /// <summary>
    /// How a value converted by the mapping of its type pair (not a nested object: a primitive, a struct, a value object)
    /// is passed to it: a value type as it is; a reference type is checked only when the destination accepts
    /// <see langword="null"/> (<see langword="null"/> → <see langword="null"/>), otherwise passed as it is, as an assignment
    /// would be. <see langword="null"/> when the source is annotated nullable and the destination is not: the value is not
    /// converted (IM0002 for the assignment).
    /// </summary>
    public static string? ForValueConversion(ITypeSymbol source, ITypeSymbol dest)
    {
        if (source.IsValueType)
        {
            return NotNull;
        }

        if (AcceptsNull(dest) || IsNullableValueType(dest))
        {
            return Check;
        }

        return source.NullableAnnotation == NullableAnnotation.Annotated ? null : NotNull;
    }

    /// <summary><c>Nullable&lt;T&gt;</c>.</summary>
    public static bool IsNullableValueType(ITypeSymbol type)
        => type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    // A reference type that is annotated nullable or has no annotation (Nullable<T> is checked separately: a nested
    // object is never Nullable<T>).
    internal static bool AcceptsNull(ITypeSymbol dest)
        => !dest.IsValueType && dest.NullableAnnotation != NullableAnnotation.NotAnnotated;
}
