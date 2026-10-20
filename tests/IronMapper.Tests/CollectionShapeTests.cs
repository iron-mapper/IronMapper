using System;
using System.Collections.Generic;
using System.Linq;
using IronMapper.Attributes;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// Every declared kind of source collection property mapped to every declared kind of destination collection property
// (List<T>, T[], IEnumerable<T>, ICollection<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, IList<T>). The property
// name is {Source}To{Destination}. Pins the behavior of the generated collection code: values, order, null, empty,
// the runtime type of the result, and that an IEnumerable<T> destination is a lazy sequence.

[MapTo(typeof(ShapeItemDto))]
public class ShapeItem
{
    public int Id { get; set; }
}

public class ShapeItemDto
{
    public int Id { get; set; }
}

public class ShapeItemSpecial : ShapeItem
{
}

[MapTo(typeof(ShapeDest))]
public class ShapeSource
{
    public List<ShapeItem>? ListToList { get; set; }
    public List<ShapeItem>? ListToArray { get; set; }
    public List<ShapeItem>? ListToEnumerable { get; set; }
    public List<ShapeItem>? ListToCollection { get; set; }
    public List<ShapeItem>? ListToReadOnlyCollection { get; set; }
    public List<ShapeItem>? ListToReadOnlyList { get; set; }
    public List<ShapeItem>? ListToIList { get; set; }
    public ShapeItem[]? ArrayToList { get; set; }
    public ShapeItem[]? ArrayToArray { get; set; }
    public ShapeItem[]? ArrayToEnumerable { get; set; }
    public ShapeItem[]? ArrayToCollection { get; set; }
    public ShapeItem[]? ArrayToReadOnlyCollection { get; set; }
    public ShapeItem[]? ArrayToReadOnlyList { get; set; }
    public ShapeItem[]? ArrayToIList { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToList { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToArray { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToEnumerable { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToCollection { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToReadOnlyCollection { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToReadOnlyList { get; set; }
    public IEnumerable<ShapeItem>? EnumerableToIList { get; set; }
    public ICollection<ShapeItem>? CollectionToList { get; set; }
    public ICollection<ShapeItem>? CollectionToArray { get; set; }
    public ICollection<ShapeItem>? CollectionToEnumerable { get; set; }
    public ICollection<ShapeItem>? CollectionToCollection { get; set; }
    public ICollection<ShapeItem>? CollectionToReadOnlyCollection { get; set; }
    public ICollection<ShapeItem>? CollectionToReadOnlyList { get; set; }
    public ICollection<ShapeItem>? CollectionToIList { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToList { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToArray { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToEnumerable { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToCollection { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToReadOnlyCollection { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToReadOnlyList { get; set; }
    public IReadOnlyCollection<ShapeItem>? ReadOnlyCollectionToIList { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToList { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToArray { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToEnumerable { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToCollection { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToReadOnlyList { get; set; }
    public IReadOnlyList<ShapeItem>? ReadOnlyListToIList { get; set; }
    public IList<ShapeItem>? IListToList { get; set; }
    public IList<ShapeItem>? IListToArray { get; set; }
    public IList<ShapeItem>? IListToEnumerable { get; set; }
    public IList<ShapeItem>? IListToCollection { get; set; }
    public IList<ShapeItem>? IListToReadOnlyCollection { get; set; }
    public IList<ShapeItem>? IListToReadOnlyList { get; set; }
    public IList<ShapeItem>? IListToIList { get; set; }
}

public class ShapeDest
{
    public List<ShapeItemDto>? ListToList { get; set; }
    public ShapeItemDto[]? ListToArray { get; set; }
    public IEnumerable<ShapeItemDto>? ListToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? ListToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? ListToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? ListToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? ListToIList { get; set; }
    public List<ShapeItemDto>? ArrayToList { get; set; }
    public ShapeItemDto[]? ArrayToArray { get; set; }
    public IEnumerable<ShapeItemDto>? ArrayToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? ArrayToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? ArrayToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? ArrayToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? ArrayToIList { get; set; }
    public List<ShapeItemDto>? EnumerableToList { get; set; }
    public ShapeItemDto[]? EnumerableToArray { get; set; }
    public IEnumerable<ShapeItemDto>? EnumerableToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? EnumerableToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? EnumerableToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? EnumerableToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? EnumerableToIList { get; set; }
    public List<ShapeItemDto>? CollectionToList { get; set; }
    public ShapeItemDto[]? CollectionToArray { get; set; }
    public IEnumerable<ShapeItemDto>? CollectionToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? CollectionToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? CollectionToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? CollectionToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? CollectionToIList { get; set; }
    public List<ShapeItemDto>? ReadOnlyCollectionToList { get; set; }
    public ShapeItemDto[]? ReadOnlyCollectionToArray { get; set; }
    public IEnumerable<ShapeItemDto>? ReadOnlyCollectionToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? ReadOnlyCollectionToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? ReadOnlyCollectionToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? ReadOnlyCollectionToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? ReadOnlyCollectionToIList { get; set; }
    public List<ShapeItemDto>? ReadOnlyListToList { get; set; }
    public ShapeItemDto[]? ReadOnlyListToArray { get; set; }
    public IEnumerable<ShapeItemDto>? ReadOnlyListToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? ReadOnlyListToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? ReadOnlyListToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? ReadOnlyListToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? ReadOnlyListToIList { get; set; }
    public List<ShapeItemDto>? IListToList { get; set; }
    public ShapeItemDto[]? IListToArray { get; set; }
    public IEnumerable<ShapeItemDto>? IListToEnumerable { get; set; }
    public ICollection<ShapeItemDto>? IListToCollection { get; set; }
    public IReadOnlyCollection<ShapeItemDto>? IListToReadOnlyCollection { get; set; }
    public IReadOnlyList<ShapeItemDto>? IListToReadOnlyList { get; set; }
    public IList<ShapeItemDto>? IListToIList { get; set; }
}

public class CollectionShapeTests
{
    public sealed record ShapeRow(
        string Name,
        Action<ShapeSource, List<ShapeItem>?> Fill,
        Func<ShapeDest, IEnumerable<ShapeItemDto>?> Read,
        string DestKind)
    {
        public override string ToString() => Name;
    }

    private static ShapeRow Row(string name, Action<ShapeSource, List<ShapeItem>?> fill,
        Func<ShapeDest, IEnumerable<ShapeItemDto>?> read, string destKind) => new(name, fill, read, destKind);

    private static readonly ShapeRow[] Rows =
    {
        Row("ListToList", (s, items) => s.ListToList = items is null ? null : items, d => d.ListToList, "List"),
        Row("ListToArray", (s, items) => s.ListToArray = items is null ? null : items, d => d.ListToArray, "Array"),
        Row("ListToEnumerable", (s, items) => s.ListToEnumerable = items is null ? null : items, d => d.ListToEnumerable, "Enumerable"),
        Row("ListToCollection", (s, items) => s.ListToCollection = items is null ? null : items, d => d.ListToCollection, "Collection"),
        Row("ListToReadOnlyCollection", (s, items) => s.ListToReadOnlyCollection = items is null ? null : items, d => d.ListToReadOnlyCollection, "ReadOnlyCollection"),
        Row("ListToReadOnlyList", (s, items) => s.ListToReadOnlyList = items is null ? null : items, d => d.ListToReadOnlyList, "ReadOnlyList"),
        Row("ListToIList", (s, items) => s.ListToIList = items is null ? null : items, d => d.ListToIList, "IList"),
        Row("ArrayToList", (s, items) => s.ArrayToList = items is null ? null : items.ToArray(), d => d.ArrayToList, "List"),
        Row("ArrayToArray", (s, items) => s.ArrayToArray = items is null ? null : items.ToArray(), d => d.ArrayToArray, "Array"),
        Row("ArrayToEnumerable", (s, items) => s.ArrayToEnumerable = items is null ? null : items.ToArray(), d => d.ArrayToEnumerable, "Enumerable"),
        Row("ArrayToCollection", (s, items) => s.ArrayToCollection = items is null ? null : items.ToArray(), d => d.ArrayToCollection, "Collection"),
        Row("ArrayToReadOnlyCollection", (s, items) => s.ArrayToReadOnlyCollection = items is null ? null : items.ToArray(), d => d.ArrayToReadOnlyCollection, "ReadOnlyCollection"),
        Row("ArrayToReadOnlyList", (s, items) => s.ArrayToReadOnlyList = items is null ? null : items.ToArray(), d => d.ArrayToReadOnlyList, "ReadOnlyList"),
        Row("ArrayToIList", (s, items) => s.ArrayToIList = items is null ? null : items.ToArray(), d => d.ArrayToIList, "IList"),
        Row("EnumerableToList", (s, items) => s.EnumerableToList = items is null ? null : Yield(items), d => d.EnumerableToList, "List"),
        Row("EnumerableToArray", (s, items) => s.EnumerableToArray = items is null ? null : Yield(items), d => d.EnumerableToArray, "Array"),
        Row("EnumerableToEnumerable", (s, items) => s.EnumerableToEnumerable = items is null ? null : Yield(items), d => d.EnumerableToEnumerable, "Enumerable"),
        Row("EnumerableToCollection", (s, items) => s.EnumerableToCollection = items is null ? null : Yield(items), d => d.EnumerableToCollection, "Collection"),
        Row("EnumerableToReadOnlyCollection", (s, items) => s.EnumerableToReadOnlyCollection = items is null ? null : Yield(items), d => d.EnumerableToReadOnlyCollection, "ReadOnlyCollection"),
        Row("EnumerableToReadOnlyList", (s, items) => s.EnumerableToReadOnlyList = items is null ? null : Yield(items), d => d.EnumerableToReadOnlyList, "ReadOnlyList"),
        Row("EnumerableToIList", (s, items) => s.EnumerableToIList = items is null ? null : Yield(items), d => d.EnumerableToIList, "IList"),
        Row("CollectionToList", (s, items) => s.CollectionToList = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToList, "List"),
        Row("CollectionToArray", (s, items) => s.CollectionToArray = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToArray, "Array"),
        Row("CollectionToEnumerable", (s, items) => s.CollectionToEnumerable = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToEnumerable, "Enumerable"),
        Row("CollectionToCollection", (s, items) => s.CollectionToCollection = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToCollection, "Collection"),
        Row("CollectionToReadOnlyCollection", (s, items) => s.CollectionToReadOnlyCollection = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToReadOnlyCollection, "ReadOnlyCollection"),
        Row("CollectionToReadOnlyList", (s, items) => s.CollectionToReadOnlyList = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToReadOnlyList, "ReadOnlyList"),
        Row("CollectionToIList", (s, items) => s.CollectionToIList = items is null ? null : new LinkedList<ShapeItem>(items), d => d.CollectionToIList, "IList"),
        Row("ReadOnlyCollectionToList", (s, items) => s.ReadOnlyCollectionToList = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToList, "List"),
        Row("ReadOnlyCollectionToArray", (s, items) => s.ReadOnlyCollectionToArray = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToArray, "Array"),
        Row("ReadOnlyCollectionToEnumerable", (s, items) => s.ReadOnlyCollectionToEnumerable = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToEnumerable, "Enumerable"),
        Row("ReadOnlyCollectionToCollection", (s, items) => s.ReadOnlyCollectionToCollection = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToCollection, "Collection"),
        Row("ReadOnlyCollectionToReadOnlyCollection", (s, items) => s.ReadOnlyCollectionToReadOnlyCollection = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToReadOnlyCollection, "ReadOnlyCollection"),
        Row("ReadOnlyCollectionToReadOnlyList", (s, items) => s.ReadOnlyCollectionToReadOnlyList = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToReadOnlyList, "ReadOnlyList"),
        Row("ReadOnlyCollectionToIList", (s, items) => s.ReadOnlyCollectionToIList = items is null ? null : new Queue<ShapeItem>(items), d => d.ReadOnlyCollectionToIList, "IList"),
        Row("ReadOnlyListToList", (s, items) => s.ReadOnlyListToList = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToList, "List"),
        Row("ReadOnlyListToArray", (s, items) => s.ReadOnlyListToArray = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToArray, "Array"),
        Row("ReadOnlyListToEnumerable", (s, items) => s.ReadOnlyListToEnumerable = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToEnumerable, "Enumerable"),
        Row("ReadOnlyListToCollection", (s, items) => s.ReadOnlyListToCollection = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToCollection, "Collection"),
        Row("ReadOnlyListToReadOnlyCollection", (s, items) => s.ReadOnlyListToReadOnlyCollection = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToReadOnlyCollection, "ReadOnlyCollection"),
        Row("ReadOnlyListToReadOnlyList", (s, items) => s.ReadOnlyListToReadOnlyList = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToReadOnlyList, "ReadOnlyList"),
        Row("ReadOnlyListToIList", (s, items) => s.ReadOnlyListToIList = items is null ? null : items.AsReadOnly(), d => d.ReadOnlyListToIList, "IList"),
        Row("IListToList", (s, items) => s.IListToList = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToList, "List"),
        Row("IListToArray", (s, items) => s.IListToArray = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToArray, "Array"),
        Row("IListToEnumerable", (s, items) => s.IListToEnumerable = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToEnumerable, "Enumerable"),
        Row("IListToCollection", (s, items) => s.IListToCollection = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToCollection, "Collection"),
        Row("IListToReadOnlyCollection", (s, items) => s.IListToReadOnlyCollection = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToReadOnlyCollection, "ReadOnlyCollection"),
        Row("IListToReadOnlyList", (s, items) => s.IListToReadOnlyList = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToReadOnlyList, "ReadOnlyList"),
        Row("IListToIList", (s, items) => s.IListToIList = items is null ? null : new System.Collections.ObjectModel.Collection<ShapeItem>(items), d => d.IListToIList, "IList"),
    };

    public static IEnumerable<object[]> AllRows => Rows.Select(r => new object[] { r.Name });

    private static ShapeRow Get(string name) => Rows.Single(r => r.Name == name);

    private static IEnumerable<ShapeItem> Yield(IEnumerable<ShapeItem> items)
    {
        foreach (var item in items) yield return item;
    }

    private static List<ShapeItem> Items(int count)
        => Enumerable.Range(1, count).Select(i => new ShapeItem { Id = i }).ToList();

    private static ShapeDest MapWith(ShapeRow row, List<ShapeItem>? items, bool inPlace)
    {
        var source = new ShapeSource();
        row.Fill(source, items);
        if (!inPlace) return source.MapToShapeDest();
        var destination = new ShapeDest();
        source.MapToShapeDest(destination);
        return destination;
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public void NestedProperty_MapsValuesInOrder(string name)
    {
        foreach (var inPlace in new[] { false, true })
        {
            var row = Get(name);
            var result = row.Read(MapWith(row, Items(5), inPlace));

            Assert.NotNull(result);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result!.Select(d => d.Id));
            AssertResultKind(row.DestKind, result);
        }
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public void NestedProperty_NullStaysNull(string name)
    {
        foreach (var inPlace in new[] { false, true })
        {
            var row = Get(name);
            Assert.Null(row.Read(MapWith(row, null, inPlace)));
        }
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public void NestedProperty_EmptyStaysEmpty(string name)
    {
        foreach (var inPlace in new[] { false, true })
        {
            var row = Get(name);
            var result = row.Read(MapWith(row, Items(0), inPlace));

            Assert.NotNull(result);
            Assert.Empty(result!);
            // LINQ returned Array.Empty for an empty array source; the type of an empty lazy sequence is not pinned.
            if (row.DestKind != "Enumerable") AssertResultKind(row.DestKind, result);
        }
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public void NestedProperty_NullElement_ThrowsArgumentNullException(string name)
    {
        var row = Get(name);
        var items = new List<ShapeItem> { new() { Id = 1 }, null! };

        // An IEnumerable<T> destination is lazy: the element mapping runs when the result is enumerated.
        var ex = Assert.Throws<ArgumentNullException>(() => row.Read(MapWith(row, items, inPlace: false))!.ToList());
        Assert.Equal("source", ex.ParamName);
    }

    private static void AssertResultKind(string destKind, IEnumerable<ShapeItemDto> result)
    {
        switch (destKind)
        {
            case "Array":
                Assert.IsType<ShapeItemDto[]>(result);
                break;
            case "Enumerable":
                // A lazy sequence, not a materialized collection.
                Assert.IsNotType<ShapeItemDto[]>(result);
                Assert.IsNotType<List<ShapeItemDto>>(result);
                break;
            default:
                Assert.IsType<List<ShapeItemDto>>(result);
                break;
        }
    }

    // ------------------------------------------------------------------
    // IEnumerable<T> destination: lazy, as in 1.1.x
    // ------------------------------------------------------------------

    [Fact]
    public void EnumerableDestination_IsLazy_SeesLaterChangesAndMapsAgainOnEachEnumeration()
    {
        var items = Items(2);
        var dto = new ShapeSource { ListToEnumerable = items }.MapToShapeDest();

        items.Add(new ShapeItem { Id = 3 });
        var first = dto.ListToEnumerable!.ToList();
        var second = dto.ListToEnumerable!.ToList();

        Assert.Equal(new[] { 1, 2, 3 }, first.Select(d => d.Id));
        Assert.NotSame(first[0], second[0]);
    }

    // ------------------------------------------------------------------
    // MapToXList / MapToXArray: every runtime kind of IEnumerable<T>
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> RuntimeSources =>
        new[] { "List", "Array", "CovariantArray", "Iterator", "LinkedList", "Queue", "ReadOnlyCollection", "HashSet" }
            .Select(k => new object[] { k });

    private static IEnumerable<ShapeItem> Source(string kind, List<ShapeItem> items) => kind switch
    {
        "List" => items,
        "Array" => items.ToArray(),
        "CovariantArray" => items.Select(i => new ShapeItemSpecial { Id = i.Id }).ToArray(),
        "Iterator" => Yield(items),
        "LinkedList" => new LinkedList<ShapeItem>(items),
        "Queue" => new Queue<ShapeItem>(items),
        "ReadOnlyCollection" => items.AsReadOnly(),
        "HashSet" => new HashSet<ShapeItem>(items),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(RuntimeSources))]
    public void MapToList_AnySource_MapsValuesInOrder(string kind)
    {
        var result = Source(kind, Items(4)).MapToShapeItemDtoList();

        Assert.IsType<List<ShapeItemDto>>(result);
        var ids = result.Select(d => d.Id);
        if (kind == "HashSet") ids = ids.OrderBy(i => i);
        Assert.Equal(new[] { 1, 2, 3, 4 }, ids);
    }

    [Theory]
    [MemberData(nameof(RuntimeSources))]
    public void MapToArray_AnySource_MapsValuesInOrder(string kind)
    {
        var result = Source(kind, Items(4)).MapToShapeItemDtoArray();

        var ids = result.Select(d => d.Id);
        if (kind == "HashSet") ids = ids.OrderBy(i => i);
        Assert.Equal(new[] { 1, 2, 3, 4 }, ids);
    }

    [Theory]
    [MemberData(nameof(RuntimeSources))]
    public void MapToListAndArray_EmptySource_ReturnEmpty(string kind)
    {
        Assert.Empty(Source(kind, Items(0)).MapToShapeItemDtoList());
        Assert.Empty(Source(kind, Items(0)).MapToShapeItemDtoArray());
    }

    [Theory]
    [MemberData(nameof(RuntimeSources))]
    public void MapToListAndArray_NullElement_ThrowsArgumentNullException(string kind)
    {
        if (kind is "CovariantArray" or "HashSet") return; // no null element in these sources
        var items = new List<ShapeItem> { new() { Id = 1 }, null! };

        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => Source(kind, items).MapToShapeItemDtoList()).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => Source(kind, items).MapToShapeItemDtoArray()).ParamName);
    }

    [Fact]
    public void MapToListAndArray_NullSource_ReturnEmpty()
    {
        IEnumerable<ShapeItem>? source = null;

        Assert.Empty(source.MapToShapeItemDtoList());
        Assert.Empty(source.MapToShapeItemDtoArray());
    }

    [Fact]
    public void MapToList_ReturnsNewListEachCall()
    {
        var items = Items(2);

        Assert.NotSame(items.MapToShapeItemDtoList(), items.MapToShapeItemDtoList());
    }
}
