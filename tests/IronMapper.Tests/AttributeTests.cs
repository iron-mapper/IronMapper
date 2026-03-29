using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Interfaces;
using Xunit;

namespace IronMapper.Tests;

public class AttributeTests
{
    // -----------------------------------------------------------------------
    // MapToAttribute
    // -----------------------------------------------------------------------

    [Fact]
    public void MapToAttribute_StoresDestinationType()
    {
        var attr = new MapToAttribute(typeof(string));
        Assert.Equal(typeof(string), attr.DestinationType);
    }

    [Fact]
    public void MapToAttribute_CanBeAppliedToClass()
    {
        var attrs = typeof(SampleSource).GetCustomAttributes<MapToAttribute>().ToArray();
        Assert.Single(attrs);
        Assert.Equal(typeof(SampleDest), attrs[0].DestinationType);
    }

    [Fact]
    public void MapToAttribute_IsSealed()
    {
        Assert.True(typeof(MapToAttribute).IsSealed);
    }

    [Fact]
    public void MapToAttribute_AllowsMultipleOnSameType()
    {
        var usage = typeof(MapToAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.True(usage.AllowMultiple);
    }

    // -----------------------------------------------------------------------
    // MapFromAttribute
    // -----------------------------------------------------------------------

    [Fact]
    public void MapFromAttribute_StoresSourceType()
    {
        var attr = new MapFromAttribute(typeof(int));
        Assert.Equal(typeof(int), attr.SourceType);
    }

    [Fact]
    public void MapFromAttribute_CanBeAppliedToClass()
    {
        var attrs = typeof(SampleDestWithMapFrom).GetCustomAttributes<MapFromAttribute>().ToArray();
        Assert.Single(attrs);
        Assert.Equal(typeof(SampleSourceForMapFrom), attrs[0].SourceType);
    }

    [Fact]
    public void MapFromAttribute_IsSealed()
    {
        Assert.True(typeof(MapFromAttribute).IsSealed);
    }

    // -----------------------------------------------------------------------
    // MapPropertyAttribute
    // -----------------------------------------------------------------------

    [Fact]
    public void MapPropertyAttribute_StoresDestinationProperty()
    {
        var attr = new MapPropertyAttribute("FullName");
        Assert.Equal("FullName", attr.DestinationProperty);
    }

    [Fact]
    public void MapPropertyAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(SampleSource).GetProperty(nameof(SampleSource.Name))!;
        var attr = prop.GetCustomAttribute<MapPropertyAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("FullName", attr!.DestinationProperty);
    }

    [Fact]
    public void MapPropertyAttribute_IsSealed()
    {
        Assert.True(typeof(MapPropertyAttribute).IsSealed);
    }

    // -----------------------------------------------------------------------
    // IgnoreAttribute
    // -----------------------------------------------------------------------

    [Fact]
    public void IgnoreAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(SampleSource).GetProperty(nameof(SampleSource.Secret))!;
        var attr = prop.GetCustomAttribute<IgnoreAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void IgnoreAttribute_IsSealed()
    {
        Assert.True(typeof(IgnoreAttribute).IsSealed);
    }

    // -----------------------------------------------------------------------
    // MapConverterAttribute
    // -----------------------------------------------------------------------

    [Fact]
    public void MapConverterAttribute_StoresConverterType()
    {
        var attr = new MapConverterAttribute(typeof(SampleConverter));
        Assert.Equal(typeof(SampleConverter), attr.ConverterType);
    }

    [Fact]
    public void MapConverterAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(SampleSource).GetProperty(nameof(SampleSource.Value))!;
        var attr = prop.GetCustomAttribute<MapConverterAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(typeof(SampleConverter), attr!.ConverterType);
    }

    [Fact]
    public void MapConverterAttribute_IsSealed()
    {
        Assert.True(typeof(MapConverterAttribute).IsSealed);
    }

    // -----------------------------------------------------------------------
    // IMapper interface contract
    // -----------------------------------------------------------------------

    [Fact]
    public void IMapper_HasMap_ObjectOverload()
    {
        var method = typeof(IMapper).GetMethods()
            .FirstOrDefault(m => m.Name == "Map" && m.GetGenericArguments().Length == 1);
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(object), parameters[0].ParameterType);
    }

    [Fact]
    public void IMapper_HasMap_TwoGenericOverload()
    {
        var methods = typeof(IMapper).GetMethods()
            .Where(m => m.Name == "Map" && m.GetGenericArguments().Length == 2)
            .ToArray();
        Assert.NotEmpty(methods);
    }

    [Fact]
    public void IMapper_HasMapCollection_Method()
    {
        var method = typeof(IMapper).GetMethod("MapCollection");
        Assert.NotNull(method);
        Assert.Equal(2, method!.GetGenericArguments().Length);
    }

    // -----------------------------------------------------------------------
    // ITypeConverter interface contract
    // -----------------------------------------------------------------------

    [Fact]
    public void ITypeConverter_Generic_HasConvertMethod()
    {
        var method = typeof(ITypeConverter<string, int>).GetMethod("Convert");
        Assert.NotNull(method);
    }

    [Fact]
    public void ITypeConverter_Generic_InheritsFromNonGeneric()
    {
        Assert.True(typeof(ITypeConverter).IsAssignableFrom(typeof(ITypeConverter<string, int>)));
    }

    // -----------------------------------------------------------------------
    // MappingProfile
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingProfile_ImplementsIMappingProfile()
    {
        Assert.True(typeof(IMappingProfile).IsAssignableFrom(typeof(MappingProfile)));
    }

    [Fact]
    public void MappingProfile_CreateMap_ReturnsIMappingExpression()
    {
        var profile = new SampleProfile();
        Assert.NotNull(profile.GetExpression());
    }

    // -----------------------------------------------------------------------
    // Exceptions
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingException_IsExceptionSubclass()
    {
        Assert.True(typeof(Exception).IsAssignableFrom(typeof(MappingException)));
    }

    [Fact]
    public void MappingConfigurationException_IsMappingExceptionSubclass()
    {
        Assert.True(typeof(MappingException).IsAssignableFrom(typeof(MappingConfigurationException)));
    }

    [Fact]
    public void MappingConfigurationException_MessageContainsTypeNames()
    {
        var ex = new MappingConfigurationException(typeof(SampleSource), typeof(SampleDest));
        Assert.Contains(nameof(SampleSource), ex.Message);
        Assert.Contains(nameof(SampleDest), ex.Message);
        Assert.Equal(typeof(SampleSource), ex.SourceType);
        Assert.Equal(typeof(SampleDest), ex.DestinationType);
    }
}

// -----------------------------------------------------------------------
// Test fixtures
// -----------------------------------------------------------------------

[MapTo(typeof(SampleDest))]
file class SampleSource
{
    [MapProperty("FullName")]
    public string Name { get; set; } = string.Empty;

    [Ignore]
    public string Secret { get; set; } = string.Empty;

    [MapConverter(typeof(SampleConverter))]
    public int Value { get; set; }
}

file class SampleDest
{
    public string FullName { get; set; } = string.Empty;
    public int Value { get; set; }
}

file sealed class SampleConverter : ITypeConverter<int, string>
{
    public string Convert(int source) => source.ToString();
}

// Separate fixtures for [MapFrom] test — no overlap with [MapTo] fixtures above.
file class SampleSourceForMapFrom { public int Id { get; set; } }

[MapFrom(typeof(SampleSourceForMapFrom))]
file class SampleDestWithMapFrom { public int Id { get; set; } }

file sealed class SampleProfile : MappingProfile
{
    private readonly IMappingExpression<SampleSource, SampleDest> _expression;

    public SampleProfile()
    {
        _expression = CreateMap<SampleSource, SampleDest>();
    }

    public IMappingExpression<SampleSource, SampleDest> GetExpression() => _expression;
}
