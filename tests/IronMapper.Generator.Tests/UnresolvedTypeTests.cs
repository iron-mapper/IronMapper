using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// A type that does not resolve (<c>typeof(Missing)</c> in <c>[MapTo]</c>/<c>[MapFrom]</c>, a type argument of
/// <c>CreateMap</c>, a converter): the compiler reports it at the user's line (CS0246 and similar); the generator skips
/// that mapping, so there is no second error inside generated code (CS0400, CS0246 in <c>.g.cs</c>). Other mappings are
/// generated. IM0003 is not reported for them: it would only repeat the compiler error, without a location. IM0003 is
/// reported for a type that resolves but cannot be mapped (open generic, array), which the compiler accepts and the
/// generator used to drop silently.
/// </summary>
public class UnresolvedTypeTests
{
    private const string OkMapping = """
        namespace App
        {
            [IronMapper.Attributes.MapTo(typeof(OkDto))] public class Ok { public int Id { get; set; } }
            public class OkDto { public int Id { get; set; } }
        }
        """;

    private static void AssertOnlyUserCodeError(string source, string expectedErrorId)
    {
        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source + "\n" + OkMapping);

        // One error, at the user's typeof/type argument; nothing from the generator and nothing in generated files.
        var error = Assert.Single(errors);
        Assert.Equal(expectedErrorId, error.Id);
        Assert.False(error.Location.SourceTree!.FilePath.EndsWith(".g.cs"), error.ToString());
        Assert.Empty(generatorDiagnostics);

        Assert.Contains(sources, s => s.Contains("MapToOkDto("));
        Assert.DoesNotContain(sources, s => s.Contains("Missing"));
    }

    [Fact]
    public void MapTo_UnknownType_OnlyCompilerError_OtherMappingsGenerated()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                [IronMapper.Attributes.MapTo(typeof(MissingDto))]
                public class Source { public int Id { get; set; } }
            }
            """, "CS0246");

    [Fact]
    public void MapFrom_UnknownType_OnlyCompilerError_OtherMappingsGenerated()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                [IronMapper.Attributes.MapFrom(typeof(MissingEntity))]
                public class Dest { public int Id { get; set; } }
            }
            """, "CS0246");

    [Fact]
    public void MapTo_UnknownTypeArgument_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                public class Box<T> { public int Id { get; set; } }

                [IronMapper.Attributes.MapTo(typeof(Box<MissingPayload>))]
                public class Source { public int Id { get; set; } }
            }
            """, "CS0246");

    [Fact]
    public void MapTo_UnknownNamespace_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                [IronMapper.Attributes.MapTo(typeof(App.MissingNamespace.Dto))]
                public class Source { public int Id { get; set; } }
            }
            """, "CS0234");

    [Fact]
    public void MapTo_InaccessibleType_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                public class Other { private class MissingHidden { public int Id { get; set; } } }

                [IronMapper.Attributes.MapTo(typeof(Other.MissingHidden))]
                public class Source { public int Id { get; set; } }
            }
            """, "CS0122");

    [Theory]
    [InlineData("MapTo", "typeof(Box<>)", "[MapTo(typeof(App.Box<>))] on 'App.Annotated' was not generated: an open generic type cannot be mapped")]
    [InlineData("MapFrom", "typeof(Box<>)", "[MapFrom(typeof(App.Box<>))] on 'App.Annotated' was not generated: an open generic type cannot be mapped")]
    [InlineData("MapTo", "typeof(int[])", "[MapTo(typeof(int[]))] on 'App.Annotated' was not generated: only classes, structs and records can be mapped")]
    [InlineData("MapFrom", "typeof(Box<int>[])", "[MapFrom(typeof(App.Box<int>[]))] on 'App.Annotated' was not generated: only classes, structs and records can be mapped")]
    public void TypeThatCompilesButCannotBeMapped_IM0003_OtherMappingsGenerated(string attribute, string type, string message)
    {
        var source = $$"""
            namespace App
            {
                public class Box<T> { public int Id { get; set; } }

                [IronMapper.Attributes.{{attribute}}({{type}})]
                public class Annotated { public int Id { get; set; } }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source + "\n" + OkMapping);

        Assert.Empty(errors);
        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(message, diagnostic.GetMessage());
        Assert.Contains(sources, s => s.Contains("MapToOkDto("));
        Assert.DoesNotContain(sources, s => s.Contains("Annotated") && s.Contains("Box"));
    }

    [Fact]
    public void MapTo_SecondAttributeWithUnknownType_FirstStillGenerated()
    {
        var source = """
            namespace App
            {
                [IronMapper.Attributes.MapTo(typeof(SourceDto))]
                [IronMapper.Attributes.MapTo(typeof(MissingDto))]
                public class Source { public int Id { get; set; } }
                public class SourceDto { public int Id { get; set; } }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Equal("CS0246", Assert.Single(errors).Id);
        Assert.Empty(generatorDiagnostics);
        Assert.Contains(sources, s => s.Contains("MapToSourceDto("));
        Assert.DoesNotContain(sources, s => s.Contains("Missing"));
    }

    [Fact]
    public void CreateMap_UnknownType_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                public class Source { public int Id { get; set; } }

                public class BrokenProfile : IronMapper.Configuration.MappingProfile
                {
                    public BrokenProfile() { CreateMap<Source, MissingDto>(); }
                }
            }
            """, "CS0246");

    [Fact]
    public void Converter_UnknownType_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                [IronMapper.Attributes.MapTo(typeof(SourceDto))]
                public class Source { [IronMapper.Attributes.MapConverter(typeof(MissingConverter))] public int Id { get; set; } }
                public class SourceDto { public int Id { get; set; } }
            }
            """, "CS0246");

    [Fact]
    public void UseConverter_UnknownType_OnlyCompilerError()
        => AssertOnlyUserCodeError("""
            namespace App
            {
                public class Source { public int Id { get; set; } }
                public class SourceDto { public int Id { get; set; } }

                public class BrokenProfile : IronMapper.Configuration.MappingProfile
                {
                    public BrokenProfile()
                    {
                        CreateMap<Source, SourceDto>().ForMember(d => d.Id, o => o.UseConverter<MissingConverter>());
                    }
                }
            }
            """, "CS0246");
}
