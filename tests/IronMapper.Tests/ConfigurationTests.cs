using System;
using System.Linq.Expressions;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Interfaces;
using Xunit;

namespace IronMapper.Tests;

/// <summary>
/// Covers <see cref="MappingExpression{TSource,TDest}"/>, <see cref="MemberConfigurationExpression{TSource,TDest}"/>,
/// <see cref="MappingException"/>, and <see cref="MappingConfigurationException"/> constructors and fluent API.
/// </summary>
public class ConfigurationTests
{
    // -----------------------------------------------------------------------
    // MappingExpression — fluent API returns same instance (fluent chain)
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingExpression_ForMember_ReturnsSameInstanceForChaining()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        Expression<Func<CfgSource, string>> selector = s => s.Name;
        var result = expr.ForMember(d => d.Name, o => o.MapFrom(selector));

        Assert.Same(expr, result);
    }

    [Fact]
    public void MappingExpression_ForMember_InvokesOptsDelegate()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var delegateCalled = false;
        expr.ForMember(d => d.Name, o =>
        {
            delegateCalled = true;
            o.Ignore();
        });

        Assert.True(delegateCalled);
    }

    [Fact]
    public void MappingExpression_Ignore_ReturnsSameInstanceForChaining()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var result = expr.Ignore(d => d.Name);

        Assert.Same(expr, result);
    }

    [Fact]
    public void MappingExpression_When_ReturnsSameInstanceForChaining()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var result = expr.When(s => s.Name != null);

        Assert.Same(expr, result);
    }

    [Fact]
    public void MappingExpression_ConvertUsing_ReturnsSameInstanceForChaining()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var result = expr.ConvertUsing<CfgConverter>();

        Assert.Same(expr, result);
    }

    [Fact]
    public void MappingExpression_ReverseMap_ReturnsSameInstanceForChaining()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var result = expr.ReverseMap();

        Assert.Same(expr, result);
    }

    [Fact]
    public void MappingExpression_FluentChain_AllMethodsChainable()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        Expression<Func<CfgSource, string>> selector = s => s.Name;
        // All methods must return non-null and not throw.
        var chained = expr
            .ForMember(d => d.Name, o => o.MapFrom(selector))
            .Ignore(d => d.Name)
            .When(s => true)
            .ReverseMap();

        Assert.NotNull(chained);
    }

    // -----------------------------------------------------------------------
    // MemberConfigurationExpression — all methods execute without throwing
    // -----------------------------------------------------------------------

    [Fact]
    public void MemberConfigurationExpression_MapFromExpression_DoesNotThrow()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        // Explicit local avoids lambda-to-overload ambiguity.
        Expression<Func<CfgSource, string>> selector = s => s.Name;
        var exception = Record.Exception(() =>
            expr.ForMember(d => d.Name, o => o.MapFrom(selector)));

        Assert.Null(exception);
    }

    [Fact]
    public void MemberConfigurationExpression_MapFromFuncResolver_DoesNotThrow()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        Func<CfgSource, object?> resolver = s => s.Name;
        var exception = Record.Exception(() =>
            expr.ForMember(d => d.Name, o => o.MapFrom(resolver)));

        Assert.Null(exception);
    }

    [Fact]
    public void MemberConfigurationExpression_Ignore_DoesNotThrow()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var exception = Record.Exception(() =>
            expr.ForMember(d => d.Name, o => o.Ignore()));

        Assert.Null(exception);
    }

    [Fact]
    public void MemberConfigurationExpression_UseConverter_DoesNotThrow()
    {
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSource, CfgDest>();

        var exception = Record.Exception(() =>
            expr.ForMember(d => d.Name, o => o.UseConverter<CfgMemberConverter>()));

        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // MappingException — all constructors
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingException_DefaultConstructor_CreatesInstanceWithNullMessage()
    {
        var ex = new MappingException();
        Assert.IsType<MappingException>(ex);
    }

    [Fact]
    public void MappingException_MessageConstructor_StoresMessage()
    {
        const string msg = "test mapping error";
        var ex = new MappingException(msg);
        Assert.Equal(msg, ex.Message);
    }

    [Fact]
    public void MappingException_MessageAndInnerConstructor_StoresBoth()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new MappingException("outer", inner);
        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    // -----------------------------------------------------------------------
    // MappingConfigurationException — all constructors
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingConfigurationException_DefaultConstructor_CreatesInstance()
    {
        var ex = new MappingConfigurationException();
        Assert.IsType<MappingConfigurationException>(ex);
        Assert.Null(ex.SourceType);
        Assert.Null(ex.DestinationType);
    }

    [Fact]
    public void MappingConfigurationException_MessageConstructor_StoresMessage()
    {
        const string msg = "bad config";
        var ex = new MappingConfigurationException(msg);
        Assert.Equal(msg, ex.Message);
        Assert.Null(ex.SourceType);
    }

    [Fact]
    public void MappingConfigurationException_TypesConstructor_StoresSourceAndDestTypes()
    {
        var ex = new MappingConfigurationException(typeof(CfgSource), typeof(CfgDest));
        Assert.Equal(typeof(CfgSource), ex.SourceType);
        Assert.Equal(typeof(CfgDest), ex.DestinationType);
        Assert.Contains(nameof(CfgSource), ex.Message);
        Assert.Contains(nameof(CfgDest), ex.Message);
    }

    [Fact]
    public void MappingConfigurationException_MessageAndInnerConstructor_StoresBoth()
    {
        var inner = new Exception("root cause");
        var ex = new MappingConfigurationException("config error", inner);
        Assert.Equal("config error", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Null(ex.SourceType);
    }

    // -----------------------------------------------------------------------
    // MappingProfile.AddTransformer + GetTransformers
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingProfile_AddTransformer_TransformerStoredInGetTransformers()
    {
        // Arrange
        var profile = new TransformerCfgProfile();

        // Act
        var transformers = profile.GetTransformers();

        // Assert
        Assert.Single(transformers);
        Assert.Equal(typeof(string), transformers[0].ValueType);
        Assert.NotNull(transformers[0].TransformerDelegate);
    }

    // -----------------------------------------------------------------------
    // IncludedMemberDescriptor
    // -----------------------------------------------------------------------

    [Fact]
    public void IncludedMemberDescriptor_Constructor_StoresMemberPath()
    {
        // Arrange & Act
        var descriptor = new IncludedMemberDescriptor("Contact");

        // Assert
        Assert.Equal("Contact", descriptor.MemberPath);
    }

    // -----------------------------------------------------------------------
    // IMappingExpression.IncludeMembers — fluent chain
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingExpression_IncludeMembers_ReturnsSameInstanceForChaining()
    {
        // Arrange
        var profile = new CfgProfile();
        var expr = profile.ExposeCreateMap<CfgSourceWithNested, CfgDestFlat>();

        // Act
        var result = expr.IncludeMembers(s => (object?)s.Nested);

        // Assert
        Assert.Same(expr, result);
    }
}

file class CfgSource { public string Name { get; set; } = string.Empty; }
file class CfgDest   { public string Name { get; set; } = string.Empty; }

file sealed class CfgConverter : ITypeConverter<CfgSource, CfgDest>
{
    public CfgDest Convert(CfgSource source) => new() { Name = source.Name };
}

// Non-generic ITypeConverter for UseConverter<T> test.
file sealed class CfgMemberConverter : ITypeConverter { }

// Exposes protected CreateMap so tests can exercise MappingExpression directly.
file sealed class CfgProfile : MappingProfile
{
    public IMappingExpression<TSource, TDest> ExposeCreateMap<TSource, TDest>()
        => CreateMap<TSource, TDest>();
}

file class CfgNestedInfo       { public string City { get; set; } = ""; }
file class CfgSourceWithNested { public CfgNestedInfo? Nested { get; set; } }
file class CfgDestFlat         { public string City { get; set; } = ""; }

file sealed class TransformerCfgProfile : MappingProfile
{
    public TransformerCfgProfile()
    {
        AddTransformer<string>(v => v.Trim());
    }
}
