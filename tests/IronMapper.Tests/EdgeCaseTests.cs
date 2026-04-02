using IronMapper.Attributes;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// -----------------------------------------------------------------------
// Fixture types — same namespace so the source generator processes [MapTo].
// -----------------------------------------------------------------------

// Empty classes
[MapTo(typeof(EmptyDest))]
public class EmptySource { }

public class EmptyDest { }

// Inheritance: source and dest both extend a base class
public class AnimalBase
{
    public string Species { get; set; } = string.Empty;
}

public class AnimalDtoBase
{
    public string Species { get; set; } = string.Empty;
}

[MapTo(typeof(DogDto))]
public class DogEntity : AnimalBase
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

public class DogDto : AnimalDtoBase
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

// Sealed source class
[MapTo(typeof(SealedDest))]
public sealed class SealedSource
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class SealedDest
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

// Destination with an extra unmapped property that should stay at its default value.
[MapTo(typeof(PartialDest))]
public class PartialSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PartialDest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Intentionally unmapped — [Ignore] suppresses IM0001.
    [Ignore]
    public int ExtraField { get; set; }
}

// -----------------------------------------------------------------------
// Tests
// -----------------------------------------------------------------------

/// <summary>
/// Verifies edge-case mapping scenarios: empty classes, inheritance, sealed sources, partial mappings.
/// </summary>
public class EdgeCaseTests
{
    [Fact]
    public void EmptySource_MapsToEmptyDest_WithoutException()
    {
        var src = new EmptySource();
        var dest = src.MapToEmptyDest();
        Assert.NotNull(dest);
    }

    [Fact]
    public void DogEntity_InheritedSpecies_IsMappedToDogDto()
    {
        var entity = new DogEntity
        {
            Species = "Canis lupus familiaris",
            Name    = "Rex",
            Age     = 3,
        };

        var dto = entity.MapToDogDto();

        Assert.Equal("Canis lupus familiaris", dto.Species);
        Assert.Equal("Rex", dto.Name);
        Assert.Equal(3, dto.Age);
    }

    [Fact]
    public void DogEntity_OwnAndInheritedProperties_AllMapped()
    {
        var entity = new DogEntity { Species = "Labrador", Name = "Buddy", Age = 5 };
        var dto = entity.MapToDogDto();

        Assert.Equal(entity.Species, dto.Species);
        Assert.Equal(entity.Name, dto.Name);
        Assert.Equal(entity.Age, dto.Age);
    }

    [Fact]
    public void SealedSource_MapsToDestCorrectly()
    {
        var src = new SealedSource { Value = 99, Label = "sealed-label" };
        var dest = src.MapToSealedDest();

        Assert.Equal(99, dest.Value);
        Assert.Equal("sealed-label", dest.Label);
    }

    [Fact]
    public void PartialMapping_MappedProperties_AreCorrect()
    {
        var src = new PartialSource { Id = 7, Name = "partial" };
        var dest = src.MapToPartialDest();

        Assert.Equal(7, dest.Id);
        Assert.Equal("partial", dest.Name);
    }

    [Fact]
    public void PartialMapping_UnmappedProperty_RemainsDefault()
    {
        var src = new PartialSource { Id = 1, Name = "x" };
        var dest = src.MapToPartialDest();

        Assert.Equal(default, dest.ExtraField);
    }
}
