using System.Collections.Immutable;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Branch-coverage tests for <see cref="MappingDescriptor"/>, <see cref="PropertyMappingDescriptor"/>,
/// and <see cref="IronMapper.Generator.Analysis.Models.DiagnosticInfo"/> equality members.
/// </summary>
public class ModelEqualityTests
{
    // -----------------------------------------------------------------------
    // PropertyMappingDescriptor
    // -----------------------------------------------------------------------

    [Fact]
    public void PropertyMappingDescriptor_EqualToSelf_ReturnsTrue()
    {
        var p = Make("Id", "Id");
        Assert.True(p.Equals(p));
    }

    [Fact]
    public void PropertyMappingDescriptor_EqualToIdenticalInstance_ReturnsTrue()
    {
        var a = Make("Id", "Id");
        var b = Make("Id", "Id");
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void PropertyMappingDescriptor_NullOther_ReturnsFalse()
    {
        var p = Make("Id", "Id");
        Assert.False(p.Equals((PropertyMappingDescriptor?)null));
    }

    [Fact]
    public void PropertyMappingDescriptor_ObjectOverload_NullReturnsFalse()
    {
        var p = Make("Id", "Id");
        Assert.False(p.Equals((object?)null));
    }

    [Fact]
    public void PropertyMappingDescriptor_ObjectOverload_WrongTypeReturnsFalse()
    {
        var p = Make("Id", "Id");
        Assert.False(p.Equals("not a descriptor"));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentSourceName_ReturnsFalse()
    {
        Assert.False(Make("Id", "Id").Equals(Make("OtherId", "Id")));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentDestName_ReturnsFalse()
    {
        Assert.False(Make("Id", "Id").Equals(Make("Id", "Identifier")));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentIsIgnored_ReturnsFalse()
    {
        var a = new PropertyMappingDescriptor("Id", "Id", isIgnored: false, converterType: null, needsNullCheck: false);
        var b = new PropertyMappingDescriptor("Id", "Id", isIgnored: true,  converterType: null, needsNullCheck: false);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentConverterType_ReturnsFalse()
    {
        var a = new PropertyMappingDescriptor("Id", "Id", false, "MyConverter", false);
        var b = new PropertyMappingDescriptor("Id", "Id", false, null,          false);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentNeedsNullCheck_ReturnsFalse()
    {
        var a = new PropertyMappingDescriptor("Id", "Id", false, null, needsNullCheck: true);
        var b = new PropertyMappingDescriptor("Id", "Id", false, null, needsNullCheck: false);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void PropertyMappingDescriptor_DifferentLambdaBody_ReturnsFalse()
    {
        var a = new PropertyMappingDescriptor("Id", "Id", false, null, false, lambdaBody: "source.X");
        var b = new PropertyMappingDescriptor("Id", "Id", false, null, false, lambdaBody: null);
        Assert.False(a.Equals(b));
    }

    // -----------------------------------------------------------------------
    // MappingDescriptor
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingDescriptor_EqualToSelf_ReturnsTrue()
    {
        var d = MakeDescriptor("Src", "Dst");
        Assert.True(d.Equals(d));
    }

    [Fact]
    public void MappingDescriptor_EqualToIdenticalInstance_ReturnsTrue()
    {
        var a = MakeDescriptor("Src", "Dst");
        var b = MakeDescriptor("Src", "Dst");
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MappingDescriptor_NullOther_ReturnsFalse()
    {
        var d = MakeDescriptor("Src", "Dst");
        Assert.False(d.Equals((MappingDescriptor?)null));
    }

    [Fact]
    public void MappingDescriptor_ObjectOverload_NullReturnsFalse()
    {
        var d = MakeDescriptor("Src", "Dst");
        Assert.False(d.Equals((object?)null));
    }

    [Fact]
    public void MappingDescriptor_DifferentSourceTypeName_ReturnsFalse()
    {
        Assert.False(MakeDescriptor("Src", "Dst").Equals(MakeDescriptor("Other", "Dst")));
    }

    [Fact]
    public void MappingDescriptor_DifferentSourceNamespace_ReturnsFalse()
    {
        var a = new MappingDescriptor("Src", "NS.A", "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, false);
        var b = new MappingDescriptor("Src", "NS.B", "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, false);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MappingDescriptor_DifferentDestTypeName_ReturnsFalse()
    {
        Assert.False(MakeDescriptor("Src", "Dst").Equals(MakeDescriptor("Src", "Other")));
    }

    [Fact]
    public void MappingDescriptor_DifferentHasCustomConverter_ReturnsFalse()
    {
        var a = new MappingDescriptor("Src", null, "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, hasCustomConverter: true);
        var b = new MappingDescriptor("Src", null, "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, hasCustomConverter: false);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MappingDescriptor_DifferentWhenConditionBody_ReturnsFalse()
    {
        var a = new MappingDescriptor("Src", null, "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, false, whenConditionBody: "source.Active");
        var b = new MappingDescriptor("Src", null, "Dst", null, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray<DiagnosticInfo>.Empty, false, whenConditionBody: null);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MappingDescriptor_DifferentPropertyMappingsCount_ReturnsFalse()
    {
        var withProp = new MappingDescriptor("Src", null, "Dst", null,
            ImmutableArray.Create(Make("Id", "Id")),
            ImmutableArray<DiagnosticInfo>.Empty, false);
        var empty = MakeDescriptor("Src", "Dst");
        Assert.False(withProp.Equals(empty));
    }

    [Fact]
    public void MappingDescriptor_DifferentPropertyMappingElement_ReturnsFalse()
    {
        var a = new MappingDescriptor("Src", null, "Dst", null,
            ImmutableArray.Create(Make("Id", "Id")),
            ImmutableArray<DiagnosticInfo>.Empty, false);
        var b = new MappingDescriptor("Src", null, "Dst", null,
            ImmutableArray.Create(Make("OtherId", "Id")),
            ImmutableArray<DiagnosticInfo>.Empty, false);
        Assert.False(a.Equals(b));
    }

    // -----------------------------------------------------------------------
    // DiagnosticInfo
    // -----------------------------------------------------------------------

    [Fact]
    public void DiagnosticInfo_EqualToSelf_ReturnsTrue()
    {
        var d = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "Prop", "Dest", "Src");
        Assert.True(d.Equals(d));
    }

    [Fact]
    public void DiagnosticInfo_EqualToIdenticalInstance_ReturnsTrue()
    {
        var a = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "Prop", "Dest", "Src");
        var b = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "Prop", "Dest", "Src");
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DiagnosticInfo_ObjectOverload_NonDiagnosticInfoReturnsFalse()
    {
        var d = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "Prop");
        Assert.False(d.Equals((object)"not a DiagnosticInfo"));
    }

    [Fact]
    public void DiagnosticInfo_DifferentDescriptorId_ReturnsFalse()
    {
        var a = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "X");
        var b = new DiagnosticInfo(DiagnosticDescriptors.IncompatiblePropertyTypes, "X");
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void DiagnosticInfo_DifferentArgsLength_ReturnsFalse()
    {
        var a = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "X");
        var b = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "X", "Y");
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void DiagnosticInfo_DifferentArgValue_ReturnsFalse()
    {
        var a = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "PropA", "Dest", "Src");
        var b = new DiagnosticInfo(DiagnosticDescriptors.UnmappedDestinationProperty, "PropB", "Dest", "Src");
        Assert.False(a.Equals(b));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PropertyMappingDescriptor Make(string src, string dest)
        => new(src, dest, isIgnored: false, converterType: null, needsNullCheck: false);

    private static MappingDescriptor MakeDescriptor(string srcName, string destName)
        => new(srcName, null, destName, null,
               ImmutableArray<PropertyMappingDescriptor>.Empty,
               ImmutableArray<DiagnosticInfo>.Empty,
               hasCustomConverter: false);
}
