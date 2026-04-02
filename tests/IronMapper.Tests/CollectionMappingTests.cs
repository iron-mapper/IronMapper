using System.Collections.Generic;
using System.Linq;
using IronMapper.Attributes;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// -----------------------------------------------------------------------
// Fixture types for MapToArray / MapToList tests
// -----------------------------------------------------------------------

[MapTo(typeof(EventDto))]
public class EventEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class EventDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------
// Fixture types for nested-collection tests
// -----------------------------------------------------------------------

[MapTo(typeof(BlogDto))]
public class BlogEntity
{
    public int Id { get; set; }
    public List<CommentEntity> Comments { get; set; } = new();
}

[MapTo(typeof(CommentDto))]
public class CommentEntity
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class BlogDto
{
    public int Id { get; set; }
    public List<CommentDto> Comments { get; set; } = new();
}

public class CommentDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------
// Fixture types for in-place mapping tests
// -----------------------------------------------------------------------

[MapTo(typeof(WorkerDto))]
public class WorkerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class WorkerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------
// Tests
// -----------------------------------------------------------------------

/// <summary>
/// Verifies the generated MapToDestTypeArray / MapToDestTypeList collection helpers
/// and nested-collection property mapping.
/// </summary>
public class CollectionMappingTests
{
    // ------------------------------------------------------------------
    // БЛОК 3 — MapToArray
    // ------------------------------------------------------------------

    [Fact]
    public void MapToArray_ReturnsCorrectArray()
    {
        var entities = new List<EventEntity>
        {
            new() { Id = 1, Title = "Alpha" },
            new() { Id = 2, Title = "Beta"  },
        };

        var dtos = entities.MapToEventDtoArray();

        Assert.Equal(2, dtos.Length);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal("Beta", dtos[1].Title);
    }

    [Fact]
    public void MapToArray_NullSource_ReturnsEmpty()
    {
        List<EventEntity>? entities = null;

        var dtos = entities.MapToEventDtoArray();

        Assert.Empty(dtos);
    }

    [Fact]
    public void MapToArray_PreservesOrder()
    {
        var entities = Enumerable.Range(1, 10)
            .Select(i => new EventEntity { Id = i, Title = $"E{i}" })
            .ToList();

        var dtos = entities.MapToEventDtoArray();

        Assert.Equal(10, dtos.Length);
        for (var i = 0; i < 10; i++)
            Assert.Equal(i + 1, dtos[i].Id);
    }

    // ------------------------------------------------------------------
    // БЛОК 3 — MapToList
    // ------------------------------------------------------------------

    [Fact]
    public void MapToList_NullSource_ReturnsEmptyList()
    {
        List<EventEntity>? entities = null;

        var dtos = entities.MapToEventDtoList();

        Assert.Empty(dtos);
    }

    [Fact]
    public void MapToList_ReturnsAllMappedItems()
    {
        var entities = new[] { new EventEntity { Id = 3, Title = "C" } };

        var dtos = entities.MapToEventDtoList();

        Assert.Single(dtos);
        Assert.Equal(3, dtos[0].Id);
        Assert.Equal("C", dtos[0].Title);
    }

    // ------------------------------------------------------------------
    // БЛОК 4 — Nested collection properties
    // ------------------------------------------------------------------

    [Fact]
    public void NestedListProperty_MapsCorrectly()
    {
        var blog = new BlogEntity
        {
            Id = 42,
            Comments = new List<CommentEntity>
            {
                new() { Id = 1, Text = "First" },
                new() { Id = 2, Text = "Second" },
            },
        };

        var dto = blog.MapToBlogDto();

        Assert.Equal(42, dto.Id);
        Assert.Equal(2, dto.Comments.Count);
        Assert.Equal(1, dto.Comments[0].Id);
        Assert.Equal("First", dto.Comments[0].Text);
        Assert.Equal("Second", dto.Comments[1].Text);
    }

    [Fact]
    public void NestedCollection_NullProperty_ReturnsNull()
    {
        var blog = new BlogEntity { Id = 1, Comments = null! };

        var dto = blog.MapToBlogDto();

        Assert.Equal(1, dto.Id);
        Assert.Null(dto.Comments);
    }

    [Fact]
    public void NestedCollection_EmptyList_ReturnsEmpty()
    {
        var blog = new BlogEntity { Id = 5, Comments = new List<CommentEntity>() };

        var dto = blog.MapToBlogDto();

        Assert.Equal(5, dto.Id);
        Assert.NotNull(dto.Comments);
        Assert.Empty(dto.Comments);
    }

    // ------------------------------------------------------------------
    // БЛОК 2 — In-place mapping (generated extension method)
    // ------------------------------------------------------------------

    [Fact]
    public void InPlace_UpdatesExistingObject()
    {
        var entity = new WorkerEntity { Id = 7, Name = "Alice" };
        var dto    = new WorkerDto   { Id = 0, Name = ""      };

        entity.MapToWorkerDto(dto);

        Assert.Equal(7, dto.Id);
        Assert.Equal("Alice", dto.Name);
    }

    [Fact]
    public void InPlace_NullDestination_ThrowsArgumentNullException()
    {
        var entity = new WorkerEntity { Id = 1, Name = "Bob" };
        Assert.Throws<global::System.ArgumentNullException>(() => entity.MapToWorkerDto(null!));
    }
}
