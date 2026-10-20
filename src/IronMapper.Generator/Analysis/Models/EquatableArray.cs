using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// Immutable array compared by its elements. <see cref="ImmutableArray{T}"/> compares by reference, so pipeline data
/// that holds one is never equal to the result of the previous run and every step after it re-runs. All collections in
/// the generator's pipeline data use this type. A default instance is treated as empty.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    /// <summary>An empty array.</summary>
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _items;

    /// <summary>Wraps <paramref name="items"/>; a default array becomes empty.</summary>
    public EquatableArray(ImmutableArray<T> items) => _items = items.IsDefault ? ImmutableArray<T>.Empty : items;

    /// <summary>The wrapped array (never default).</summary>
    public ImmutableArray<T> AsImmutableArray() => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    /// <summary>Number of elements.</summary>
    public int Length => _items.IsDefault ? 0 : _items.Length;

    /// <inheritdoc/>
    public int Count => Length;

    /// <inheritdoc/>
    public T this[int index] => _items[index];

    /// <summary>Wraps an <see cref="ImmutableArray{T}"/>.</summary>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other)
    {
        var left = AsImmutableArray();
        var right = other.AsImmutableArray();
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsImmutableArray())
            hash = hash * 31 + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item));
        return hash;
    }

    /// <summary>Value equality.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>Enumerates the elements without boxing.</summary>
    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();
}
