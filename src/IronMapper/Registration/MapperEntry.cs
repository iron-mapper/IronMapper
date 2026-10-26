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
        Func<IEnumerable<TSource>?, List<TDest>>? mapList,
        Func<IEnumerable<TSource>?, TDest[]>? mapArray)
        : base(typeof(TSource), typeof(TDest))
    {
        Map = source => map((TSource)source);
        MapInto = (source, destination) => mapInto((TSource)source, (TDest)destination);
        // MapToXList/MapToXArray can be turned off (IronMapperGenerateList/Array, GenerateList/Array = false); then the
        // registry maps the elements with MapToX itself, with the same result (a null source gives an empty result).
        MapList = mapList is not null
            ? source => mapList((IEnumerable<TSource>)source)
            : source => ToList((IEnumerable<TSource>)source, map);
        MapArray = mapArray is not null
            ? source => mapArray((IEnumerable<TSource>)source)
            : source => ToArray((IEnumerable<TSource>)source, map);
    }

    private static List<TDest> ToList(IEnumerable<TSource>? source, Func<TSource, TDest> map)
    {
        if (source is null)
        {
            return new List<TDest>();
        }

        var result = source is ICollection<TSource> collection ? new List<TDest>(collection.Count) : new List<TDest>();
        foreach (var item in source)
        {
            result.Add(map(item));
        }

        return result;
    }

    private static TDest[] ToArray(IEnumerable<TSource>? source, Func<TSource, TDest> map)
    {
        if (source is null)
        {
            return Array.Empty<TDest>();
        }

        if (source is ICollection<TSource> collection)
        {
            var result = new TDest[collection.Count];
            int i = 0;
            foreach (var item in collection)
            {
                result[i++] = map(item);
            }

            return result;
        }

        return ToList(source, map).ToArray();
    }

    public override Func<object, object?> Map { get; }

    public override Action<object, object> MapInto { get; }

    public override Func<object, object?> MapList { get; }

    public override Func<object, object?> MapArray { get; }

    public override bool IsSourceCollection(Type collectionType)
        => typeof(IEnumerable<TSource>).IsAssignableFrom(collectionType);
}
