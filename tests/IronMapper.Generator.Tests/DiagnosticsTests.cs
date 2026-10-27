using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Tests that the generator emits the correct IM-series diagnostics for various
/// misconfiguration scenarios.
/// </summary>
public class DiagnosticsTests
{
    // -----------------------------------------------------------------------
    // IM0001 — Unmapped destination property
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0001_UnmappedDestinationProperty_EmitsWarning()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(Dest))]
            public class Source { public string Name { get; set; } = ""; }

            public class Dest
            {
                public string Name { get; set; } = "";
                public int ExtraProperty { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0001 = diagnostics.Where(d => d.Id == "IM0001").ToList();
        Assert.NotEmpty(im0001);
        Assert.Contains(im0001, d => d.GetMessage().Contains("ExtraProperty"));
        Assert.All(im0001, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void IM0001_WhenDestPropertyHasIgnoreAttribute_NoWarning()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(Dest))]
            public class Source { public string Name { get; set; } = ""; }

            public class Dest
            {
                public string Name { get; set; } = "";
                [Ignore]
                public int ExtraProperty { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "IM0001");
    }

    // -----------------------------------------------------------------------
    // IM0005 — Empty MappingProfile
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0005_EmptyMappingProfile_EmitsWarning()
    {
        string source = """
            using IronMapper.Configuration;

            public class MyEmptyProfile : MappingProfile
            {
                public MyEmptyProfile() { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0005 = diagnostics.Where(d => d.Id == "IM0005").ToList();
        Assert.NotEmpty(im0005);
        Assert.Contains(im0005, d => d.GetMessage().Contains("MyEmptyProfile"));
        Assert.All(im0005, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void IM0005_ProfileWithMappings_NoWarning()
    {
        string source = """
            using IronMapper.Configuration;

            public class Src { public int Id { get; set; } }
            public class Dst { public int Id { get; set; } }

            public class MyProfile : MappingProfile
            {
                public MyProfile()
                {
                    CreateMap<Src, Dst>();
                }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "IM0005");
    }

    // -----------------------------------------------------------------------
    // IM0006 — [MapProperty] destination not found
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0006_MapPropertyDestNotFound_EmitsError()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(Dest))]
            public class Source
            {
                [MapProperty("NonExistentProp")]
                public string Name { get; set; } = "";
            }

            public class Dest
            {
                public string FullName { get; set; } = "";
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0006 = diagnostics.Where(d => d.Id == "IM0006").ToList();
        Assert.NotEmpty(im0006);
        Assert.Contains(im0006, d => d.GetMessage().Contains("NonExistentProp"));
        Assert.All(im0006, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void IM0006_MapPropertyDestExists_NoError()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(Dest))]
            public class Source
            {
                [MapProperty("FullName")]
                public string Name { get; set; } = "";
            }

            public class Dest
            {
                public string FullName { get; set; } = "";
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "IM0006");
    }

    // -----------------------------------------------------------------------
    // IM0008 — Abstract / interface destination
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0008_AbstractDestinationType_EmitsError()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(AbstractDest))]
            public class Source { public int Id { get; set; } }

            public abstract class AbstractDest { public int Id { get; set; } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0008 = diagnostics.Where(d => d.Id == "IM0008").ToList();
        Assert.NotEmpty(im0008);
        Assert.Contains(im0008, d => d.GetMessage().Contains("AbstractDest"));
        Assert.All(im0008, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void IM0008_InterfaceDestinationType_EmitsError()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(IDest))]
            public class Source { public int Id { get; set; } }

            public interface IDest { int Id { get; set; } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0008 = diagnostics.Where(d => d.Id == "IM0008").ToList();
        Assert.NotEmpty(im0008);
        Assert.Contains(im0008, d => d.GetMessage().Contains("IDest"));
    }

    [Fact]
    public void IM0008_ProfileWithAbstractDest_EmitsError()
    {
        string source = """
            using IronMapper.Configuration;

            public class Src { public int Id { get; set; } }
            public abstract class AbstractDst { public int Id { get; set; } }

            public class MyProfile : MappingProfile
            {
                public MyProfile()
                {
                    CreateMap<Src, AbstractDst>();
                }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0008 = diagnostics.Where(d => d.Id == "IM0008").ToList();
        Assert.NotEmpty(im0008);
        Assert.Contains(im0008, d => d.GetMessage().Contains("AbstractDst"));
    }

    // -----------------------------------------------------------------------
    // IM0009 — Constructor parameter not mapped (an error from 2.1.0: the destination is created by its constructor)
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0009_RecordDestWithUnmappedParameter_IsAnError_MappingNotGenerated()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(DestRecord))]
            public class Source
            {
                public string Name { get; set; } = "";
            }

            public record DestRecord(string Name, int MissingFromSource);
            """;

        var (generated, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0009 = Assert.Single(diagnostics, d => d.Id == "IM0009");
        Assert.Contains("MissingFromSource", im0009.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, im0009.Severity);
        Assert.DoesNotContain(generated, s => s.Contains("MapToDestRecord("));
    }

    [Fact]
    public void IM0009_RecordDestAllParametersMapped_NoWarning()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(DestRecord))]
            public class Source
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }

            public record DestRecord(string Name, int Age);
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "IM0009");
    }

    // -----------------------------------------------------------------------
    // No warnings when [Ignore] suppresses IM0001
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0001_WhenSourcePropertyIsIgnored_DiagnosticNotEmitted()
    {
        string source = """
            using IronMapper.Attributes;

            [MapTo(typeof(OrderDto))]
            public class Order
            {
                public int Id { get; set; }

                [Ignore]
                public string InternalToken { get; set; } = "";
            }

            public class OrderDto
            {
                public int Id { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "IM0001");
    }

    // -----------------------------------------------------------------------
    // IM0005 — Empty profile produces no generated source
    // -----------------------------------------------------------------------

    [Fact]
    public void IM0005_EmptyProfile_ProducesNoGeneratedSource()
    {
        string source = """
            using IronMapper.Configuration;

            public class EmptyProfile : MappingProfile
            {
                public EmptyProfile() { }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(generatedSources, s => s.Contains("MapTo"));
        Assert.Contains(diagnostics, d => d.Id == "IM0005");
    }
}
