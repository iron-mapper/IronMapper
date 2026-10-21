using System;
using System.Collections.Generic;

namespace IronMapper.Registration;

/// <summary>
/// The generated delegates of one mapping as <see cref="object"/>-typed wrappers, for lookups by
/// <see cref="Type"/> (runtime type of the source, base types, collections).
/// </summary>
internal abstract class MapperEntry
{
    protected MapperEntry(Type sourceType, Type destinationType)
    {
        SourceType = sourceType;
        DestinationType = destinationType;
    }

    public Type SourceType { get; }

    public Type DestinationType { get; }

    /// <summary><c>MapToX(source)</c>.</summary>
    public abstract Func<object, object?> Map { get; }

    /// <summary><c>MapToX(source, destination)</c>.</summary>
    public abstract Action<object, object> MapInto { get; }

    /// <summary><c>MapToXList(source)</c>: a <c>List&lt;TDest&gt;</c> from any <c>IEnumerable&lt;TSource&gt;</c>.</summary>
    public abstract Func<object, object?> MapList { get; }

    /// <summary><c>MapToXArray(source)</c>: a <c>TDest[]</c> from any <c>IEnumerable&lt;TSource&gt;</c>.</summary>
    public abstract Func<object, object?> MapArray { get; }

    /// <summary>Whether a value of <paramref name="collectionType"/> is an <c>IEnumerable&lt;TSource&gt;</c>.</summary>
    public abstract bool IsSourceCollection(Type collectionType);
}

internal sealed class MapperEntry<TSource, TDest> : MapperEntry
{
    public MapperEntry(
        Func<TSource, TDest> map,
        Action<TSource, TDest> mapInto,
        Func<IEnumerable<TSource>?, List<TDest>> mapList,
        Func<IEnumerable<TSource>?, TDest[]> mapArray)
        : base(typeof(TSource), typeof(TDest))
    {
        Map = source => map((TSource)source);
        MapInto = (source, destination) => mapInto((TSource)source, (TDest)destination);
        MapList = source => mapList((IEnumerable<TSource>)source);
        MapArray = source => mapArray((IEnumerable<TSource>)source);
    }

    public override Func<object, object?> Map { get; }

    public override Action<object, object> MapInto { get; }

    public override Func<object, object?> MapList { get; }

    public override Func<object, object?> MapArray { get; }

    public override bool IsSourceCollection(Type collectionType)
        => typeof(IEnumerable<TSource>).IsAssignableFrom(collectionType);
}
