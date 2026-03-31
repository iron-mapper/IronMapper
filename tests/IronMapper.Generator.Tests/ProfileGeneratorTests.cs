using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Verifies that <see cref="IronMapperGenerator"/> correctly discovers
/// <c>MappingProfile</c> subclasses and emits the expected mapping code.
/// Also covers edge-case branches in <c>ProfileAnalyzer</c>.
/// </summary>
public class ProfileGeneratorTests
{
    // ------------------------------------------------------------------
    // Profile detection
    // ------------------------------------------------------------------

    [Fact]
    public void ProfileDetection_ClassInheritingMappingProfile_GeneratesMapper()
    {
        var source = """
            using IronMapper.Configuration;

            public class ProductEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class ProductDto    { public int Id { get; set; } public string Name { get; set; } = ""; }

            public class ProductProfile : MappingProfile
            {
                public ProductProfile()
                {
                    CreateMap<ProductEntity, ProductDto>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToProductDto", code);
        Assert.Contains("Id = source.Id", code);
        Assert.Contains("Name = source.Name", code);
    }

    // ------------------------------------------------------------------
    // ForMember + MapFrom lambda
    // ------------------------------------------------------------------

    [Fact]
    public void ForMemberInCode_ForMemberMapFrom_EmitsLambdaBodyInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class UserEntity
            {
                public string FirstName { get; set; } = "";
                public string LastName  { get; set; } = "";
            }

            public class UserDto
            {
                public string FullName { get; set; } = "";
            }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserEntity, UserDto>()
                        .ForMember(dest => dest.FullName,
                                   opt  => opt.MapFrom(src => src.FirstName + " " + src.LastName));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToUserDto", code);
        // The lambda body should be emitted verbatim (with "source" as the param name).
        Assert.Contains("source.FirstName", code);
        Assert.Contains("source.LastName", code);
        Assert.Contains("FullName =", code);
    }

    // ------------------------------------------------------------------
    // ForMember + Ignore
    // ------------------------------------------------------------------

    [Fact]
    public void ForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class InvoiceEntity { public int Id { get; set; } public string Secret { get; set; } = ""; }
            public class InvoiceDto    { public int Id { get; set; } public string Secret { get; set; } = ""; }

            public class InvoiceProfile : MappingProfile
            {
                public InvoiceProfile()
                {
                    CreateMap<InvoiceEntity, InvoiceDto>()
                        .ForMember(dest => dest.Secret, opt => opt.Ignore());
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Id = source.Id", code);
        Assert.DoesNotContain("Secret = source.Secret", code);
    }

    // ------------------------------------------------------------------
    // When condition
    // ------------------------------------------------------------------

    [Fact]
    public void WhenCondition_WhenClause_EmittedAsGuardInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class ItemEntity { public int Id { get; set; } public bool IsActive { get; set; } }
            public class ItemDto    { public int Id { get; set; } public bool IsActive { get; set; } }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile()
                {
                    CreateMap<ItemEntity, ItemDto>().When(src => src.IsActive);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        // Guard should appear before the return statement.
        Assert.Contains("if (!", code);
        Assert.Contains("source.IsActive", code);
        Assert.Contains("return default", code);
    }

    // ------------------------------------------------------------------
    // ReverseMap
    // ------------------------------------------------------------------

    [Fact]
    public void ReverseMap_ReverseMapCall_GeneratesBothDirections()
    {
        var source = """
            using IronMapper.Configuration;

            public class CatEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class CatDto    { public int Id { get; set; } public string Name { get; set; } = ""; }

            public class CatProfile : MappingProfile
            {
                public CatProfile()
                {
                    CreateMap<CatEntity, CatDto>().ReverseMap();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToCatDto", code);
        Assert.Contains("MapToCatEntity", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: empty constructor / no CreateMap calls
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyConstructor_NoCreateMapCalls_ProducesNoGeneratedSource()
    {
        // A MappingProfile subclass with an empty constructor should not crash
        // and should not produce any generated mapping files.
        var source = """
            using IronMapper.Configuration;

            public class NodeEntity { public int Id { get; set; } }
            public class NodeDto    { public int Id { get; set; } }

            public class EmptyProfile : MappingProfile
            {
                public EmptyProfile() { }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // No CreateMap — nothing should be generated by the profile pipeline.
        Assert.DoesNotContain(generatedSources, s => s.Contains("MapToNodeDto"));
    }

    // ------------------------------------------------------------------
    // Branch-coverage: parenthesised lambda in ForMember dest selector
    // ------------------------------------------------------------------

    [Fact]
    public void ForMember_ParenthesisedDestLambda_ExtractsPropertyNameCorrectly()
    {
        // Uses (dest) => dest.FullName (parenthesised) instead of dest => dest.FullName (simple).
        var source = """
            using IronMapper.Configuration;

            public class EmpEntity { public string First { get; set; } = ""; public string Last { get; set; } = ""; }
            public class EmpDto    { public string FullName { get; set; } = ""; }

            public class EmpProfile : MappingProfile
            {
                public EmpProfile()
                {
                    CreateMap<EmpEntity, EmpDto>()
                        .ForMember((dest) => dest.FullName,
                                   (opt)  => opt.MapFrom((src) => src.First + " " + src.Last));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("FullName =", code);
        Assert.Contains("source.First", code);
        Assert.Contains("source.Last", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: lambda param already named "source" (ReplaceIdentifier no-op)
    // ------------------------------------------------------------------

    [Fact]
    public void ForMember_LambdaParamAlreadyNamedSource_EmitsBodyUnchanged()
    {
        // When the lambda parameter is already called "source", ReplaceIdentifier
        // should be a no-op and the body should still be emitted correctly.
        var source = """
            using IronMapper.Configuration;

            public class BoxEntity { public int Width { get; set; } public int Height { get; set; } }
            public class BoxDto    { public int Area { get; set; } }

            public class BoxProfile : MappingProfile
            {
                public BoxProfile()
                {
                    CreateMap<BoxEntity, BoxDto>()
                        .ForMember(dest => dest.Area,
                                   opt  => opt.MapFrom(source => source.Width * source.Height));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Area =", code);
        Assert.Contains("source.Width", code);
        Assert.Contains("source.Height", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: MappingAnalyzer — [MapProperty] + [Ignore] on the same source property
    // ------------------------------------------------------------------

    [Fact]
    public void MappingAnalyzer_MapPropertyAndIgnoreOnSameSourceProp_PropertyIsIgnored()
    {
        // When [MapProperty] remaps a source property to a dest property AND [Ignore] is also
        // present, the mapping for that destination property should be skipped in the output.
        var source = """
            using IronMapper.Attributes;

            [MapTo(typeof(ReportDto))]
            public class ReportEntity
            {
                public int Id { get; set; }

                [MapProperty("Title")]
                [Ignore]
                public string InternalCode { get; set; } = "";
            }

            public class ReportDto
            {
                public int Id { get; set; }
                public string Title { get; set; } = "";
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Id = source.Id", code);
        // Title is mapped from InternalCode which carries [Ignore] — must not appear in output.
        Assert.DoesNotContain("Title = source.InternalCode", code);
    }
}
