using System;
using System.Collections.Generic;
using System.Linq;
using IronMapper.Attributes;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// Fixture types — same namespace so the source generator processes [MapTo].

[MapTo(typeof(ItemDto))]
public class ItemEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Tests mapping over IEnumerable collections: null, empty, large, and nested lists.
/// </summary>
public class CollectionEdgeCaseTests
{
    [Fact]
    public void NullCollection_Select_ThrowsArgumentNullException()
    {
        List<ItemEntity>? items = null;
        Assert.Throws<ArgumentNullException>(() => items!.Select(e => e.MapToItemDto()).ToList());
    }

    [Fact]
    public void EmptyCollection_MapsToEmptyList()
    {
        var items = new List<ItemEntity>();
        var dtos = items.Select(e => e.MapToItemDto()).ToList();

        Assert.Empty(dtos);
    }

    [Fact]
    public void Collection_AllItemsMapped()
    {
        var items = new List<ItemEntity>
        {
            new() { Id = 1, Name = "A" },
            new() { Id = 2, Name = "B" },
            new() { Id = 3, Name = "C" },
        };

        var dtos = items.Select(e => e.MapToItemDto()).ToList();

        Assert.Equal(3, dtos.Count);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal("B", dtos[1].Name);
        Assert.Equal(3, dtos[2].Id);
    }

    [Fact]
    public void LargeCollection_10000Elements_AllMapped()
    {
        var items = Enumerable.Range(1, 10_000)
            .Select(i => new ItemEntity { Id = i, Name = $"Item{i}" })
            .ToList();

        var dtos = items.Select(e => e.MapToItemDto()).ToList();

        Assert.Equal(10_000, dtos.Count);
        Assert.Equal(1,      dtos.First().Id);
        Assert.Equal(10_000, dtos.Last().Id);
        Assert.Equal("Item5000", dtos[4_999].Name);
    }

    [Fact]
    public void Collection_OrderIsPreserved()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => new ItemEntity { Id = i })
            .ToList();

        var dtos = items.Select(e => e.MapToItemDto()).ToList();

        for (var i = 0; i < 100; i++)
            Assert.Equal(i, dtos[i].Id);
    }

    [Fact]
    public void NestedCollections_AllGroupsMapped()
    {
        var groups = new List<List<ItemEntity>>
        {
            new() { new() { Id = 1, Name = "x" }, new() { Id = 2, Name = "y" } },
            new() { new() { Id = 3, Name = "z" } },
        };

        var dtosGroups = groups
            .Select(g => g.Select(e => e.MapToItemDto()).ToList())
            .ToList();

        Assert.Equal(2, dtosGroups.Count);
        Assert.Equal(2, dtosGroups[0].Count);
        Assert.Single(dtosGroups[1]);
        Assert.Equal("x", dtosGroups[0][0].Name);
        Assert.Equal(3, dtosGroups[1][0].Id);
    }
}
