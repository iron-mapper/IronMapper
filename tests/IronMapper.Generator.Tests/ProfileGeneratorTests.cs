using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Verifies that <see cref="IronMapperGenerator"/> correctly discovers
/// <c>MappingProfile</c> subclasses and emits the expected mapping code.
/// </summary>
public class ProfileGeneratorTests
{
    // ------------------------------------------------------------------
    // Profile detection
    // ------------------------------------------------------------------

    [Fact]
    public void TestProfileDetection_ClassInheritingMappingProfile_GeneratesMapper()
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
    public void TestForMemberInCode_ForMemberMapFrom_EmitsLambdaBodyInGeneratedCode()
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
    public void TestForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode()
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
    public void TestWhenCondition_WhenClause_EmittedAsGuardInGeneratedCode()
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
    public void TestReverseMap_ReverseMapCall_GeneratesBothDirections()
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
}
