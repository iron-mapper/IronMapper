using System;
using IronMapper.Attributes;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// Non-file-scoped types in the same namespace so the source generator can process [MapTo].

[MapTo(typeof(PrimitivesDto))]
public class PrimitivesEntity
{
    public string StringVal { get; set; } = string.Empty;
    public int IntVal { get; set; }
    public bool BoolVal { get; set; }
    public decimal DecimalVal { get; set; }
    public DateTime DateVal { get; set; }
    public Guid GuidVal { get; set; }
    public DayOfWeek EnumVal { get; set; }
    public long LongVal { get; set; }
    public double DoubleVal { get; set; }
    public float FloatVal { get; set; }
}

public class PrimitivesDto
{
    public string StringVal { get; set; } = string.Empty;
    public int IntVal { get; set; }
    public bool BoolVal { get; set; }
    public decimal DecimalVal { get; set; }
    public DateTime DateVal { get; set; }
    public Guid GuidVal { get; set; }
    public DayOfWeek EnumVal { get; set; }
    public long LongVal { get; set; }
    public double DoubleVal { get; set; }
    public float FloatVal { get; set; }
}

/// <summary>
/// Verifies that the generator correctly maps all primitive and common value types.
/// </summary>
public class SimpleTypesTests
{
    private static PrimitivesEntity CreateEntity() => new()
    {
        StringVal  = "hello",
        IntVal     = 42,
        BoolVal    = true,
        DecimalVal = 3.14m,
        DateVal    = new DateTime(2024, 6, 15),
        GuidVal    = new Guid("12345678-1234-1234-1234-123456789012"),
        EnumVal    = DayOfWeek.Wednesday,
        LongVal    = 9_999_999_999L,
        DoubleVal  = 2.718,
        FloatVal   = 1.41421f,
    };

    [Fact]
    public void PrimitivesMapping_StringProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal("hello", dto.StringVal);
    }

    [Fact]
    public void PrimitivesMapping_IntProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(42, dto.IntVal);
    }

    [Fact]
    public void PrimitivesMapping_BoolProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.True(dto.BoolVal);
    }

    [Fact]
    public void PrimitivesMapping_DecimalProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(3.14m, dto.DecimalVal);
    }

    [Fact]
    public void PrimitivesMapping_DateTimeProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(new DateTime(2024, 6, 15), dto.DateVal);
    }

    [Fact]
    public void PrimitivesMapping_GuidProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(new Guid("12345678-1234-1234-1234-123456789012"), dto.GuidVal);
    }

    [Fact]
    public void PrimitivesMapping_EnumProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(DayOfWeek.Wednesday, dto.EnumVal);
    }

    [Fact]
    public void PrimitivesMapping_LongProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(9_999_999_999L, dto.LongVal);
    }

    [Fact]
    public void PrimitivesMapping_DoubleProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(2.718, dto.DoubleVal);
    }

    [Fact]
    public void PrimitivesMapping_FloatProperty_IsMappedCorrectly()
    {
        var dto = CreateEntity().MapToPrimitivesDto();
        Assert.Equal(1.41421f, dto.FloatVal);
    }
}
