using System;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// Which optional overloads are generated for a mapping. <c>MapToX(source)</c> is always generated: nested collection
/// properties and the <c>IMapper</c> registration call it.
/// </summary>
internal sealed class GeneratedMethods : IEquatable<GeneratedMethods>
{
    /// <summary>Everything (the default).</summary>
    public static readonly GeneratedMethods All = new(inPlace: true, list: true, array: true);

    public GeneratedMethods(bool inPlace, bool list, bool array)
    {
        InPlace = inPlace;
        List = list;
        Array = array;
    }

    /// <summary>The public in-place overload <c>MapToX(source, destination)</c>.</summary>
    public bool InPlace { get; }

    /// <summary><c>MapToXList</c>.</summary>
    public bool List { get; }

    /// <summary><c>MapToXArray</c>.</summary>
    public bool Array { get; }

    /// <summary>These values with the ones set by <paramref name="overrides"/> replaced.</summary>
    public GeneratedMethods With(GeneratedMethodOverrides overrides)
        => overrides.IsEmpty
            ? this
            : new GeneratedMethods(overrides.InPlace ?? InPlace, overrides.List ?? List, overrides.Array ?? Array);

    /// <inheritdoc/>
    public bool Equals(GeneratedMethods? other)
        => other is not null && InPlace == other.InPlace && List == other.List && Array == other.Array;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratedMethods);

    /// <inheritdoc/>
    public override int GetHashCode() => (InPlace ? 1 : 0) | (List ? 2 : 0) | (Array ? 4 : 0);
}

/// <summary>
/// The overloads set for one mapping (<c>GenerateInPlace</c>/<c>GenerateList</c>/<c>GenerateArray</c> of
/// <c>[MapTo]</c>/<c>[MapFrom]</c>, <c>GenerateMethods(...)</c> in a profile); <see langword="null"/> means not set, so
/// the value for the assembly applies. The default instance sets nothing.
/// </summary>
internal readonly struct GeneratedMethodOverrides : IEquatable<GeneratedMethodOverrides>
{
    public GeneratedMethodOverrides(bool? inPlace, bool? list, bool? array)
    {
        InPlace = inPlace;
        List = list;
        Array = array;
    }

    public bool? InPlace { get; }

    public bool? List { get; }

    public bool? Array { get; }

    /// <summary>Nothing is set.</summary>
    public bool IsEmpty => InPlace is null && List is null && Array is null;

    /// <inheritdoc/>
    public bool Equals(GeneratedMethodOverrides other)
        => InPlace == other.InPlace && List == other.List && Array == other.Array;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GeneratedMethodOverrides other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => ((InPlace.GetHashCode() * 31) + List.GetHashCode()) * 31 + Array.GetHashCode();
}
