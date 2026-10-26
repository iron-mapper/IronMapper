using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Lambdas from MappingProfile constructors are rewritten into the generated class, which has no
/// <c>using</c> directives and no access to the profile instance. Names must be fully qualified
/// through the semantic model; unsupported references must produce IronMapper diagnostics instead
/// of C# errors in the generated file. All tests compile the generated code.
/// </summary>
public class ProfileLambdaGeneratorTests
{
    private static string Join(IReadOnlyList<string> sources) => string.Join("\n", sources);

    private static void AssertCompiles(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Compilation errors:\n" + string.Join("\n", errors.Select(e => e.ToString())));

    // ------------------------------------------------------------------
    // Static methods of the profile
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    public void StaticProfileMethod_AccessibleFromGeneratedCode_IsCalledWithFullName(string modifier)
    {
        string source = $$"""
            using System;
            using IronMapper.Configuration;

            namespace App;

            public class Shift { public TimeSpan Grace { get; set; } }
            public class ShiftDto { public int GraceSeconds { get; set; } }

            public class ShiftProfile : MappingProfile
            {
                public ShiftProfile()
                {
                    CreateMap<Shift, ShiftDto>()
                        .ForMember(d => d.GraceSeconds, o => o.MapFrom(s => ToSeconds(s.Grace)));
                }

                {{modifier}} static int ToSeconds(TimeSpan value) => (int)value.TotalSeconds;
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        Assert.Contains("GraceSeconds = global::App.ShiftProfile.ToSeconds(source.Grace)", Join(generated));
    }

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    public void StaticProfileMethod_NotAccessible_ReportsIM0011_AndOtherMappingsStillGenerate(string modifier)
    {
        string source = $$"""
            using System;
            using IronMapper.Configuration;

            namespace App;

            public class Shift { public TimeSpan Grace { get; set; } public int Id { get; set; } }
            public class ShiftDto { public int GraceSeconds { get; set; } }
            public class ShiftSummary { public int Id { get; set; } }

            public class ShiftProfile : MappingProfile
            {
                public ShiftProfile()
                {
                    CreateMap<Shift, ShiftDto>()
                        .ForMember(d => d.GraceSeconds, o => o.MapFrom(s => ToSeconds(s.Grace)));
                    CreateMap<Shift, ShiftSummary>();
                }

                {{modifier}} static int ToSeconds(TimeSpan value) => (int)value.TotalSeconds;
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        string message = diagnostic.GetMessage();
        Assert.Contains("App.ShiftProfile", message);
        Assert.Contains("ToSeconds", message);
        Assert.Contains(modifier, message);
        Assert.Contains("internal", message);

        AssertCompiles(errors);
        string code = Join(generated);
        Assert.DoesNotContain("MapToShiftDto", code);
        Assert.Contains("MapToShiftSummary", code);
    }

    // ------------------------------------------------------------------
    // Types and extension methods imported by `using` in the profile file
    // ------------------------------------------------------------------

    [Fact]
    public void TypeAndExtensionMethodFromProfileUsings_AreFullyQualified()
    {
        string source = """
            using IronMapper.Configuration;
            using App.Text;

            namespace App.Text
            {
                public static class SlugExtensions
                {
                    public static string ToSlug(this string value) => value.ToLowerInvariant().Replace(' ', '-');
                    public static string Wrap<T>(this T value, string around) => around + value + around;
                }

                public static class Formatter
                {
                    public static string Upper(string value) => value.ToUpperInvariant();
                }
            }

            namespace App
            {
                public class Article { public string Title { get; set; } = ""; public int Id { get; set; } }
                public class ArticleDto { public string Slug { get; set; } = ""; public string Caption { get; set; } = ""; public string Tag { get; set; } = ""; }

                public class ArticleProfile : MappingProfile
                {
                    public ArticleProfile()
                    {
                        CreateMap<Article, ArticleDto>()
                            .ForMember(d => d.Slug, o => o.MapFrom(s => s.Title.ToSlug()))
                            .ForMember(d => d.Caption, o => o.MapFrom(s => Formatter.Upper(s.Title)))
                            .ForMember(d => d.Tag, o => o.MapFrom(s => s.Id.Wrap<int>("#")));
                    }
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.Contains("Slug = global::App.Text.SlugExtensions.ToSlug(source.Title)", code);
        Assert.Contains("Caption = global::App.Text.Formatter.Upper(source.Title)", code);
        Assert.Contains("Tag = global::App.Text.SlugExtensions.Wrap<int>(source.Id, \"#\")", code);
    }

    [Fact]
    public void ExtensionCallAfterNullConditional_IsEmittedAsWritten()
    {
        // Extension methods after `?.` are not rewritten (their receiver only exists inside the
        // conditional access). `?.` is not allowed in MapFrom (an expression tree, CS8072), but it is
        // in Func-based lambdas such as When. An extension class in the global namespace is visible everywhere.
        string source = """
            using IronMapper.Configuration;

            public static class GlobalTextExtensions
            {
                public static string Shout(this string value) => value.ToUpperInvariant();
            }

            namespace App
            {
                public class Inner { public string Name { get; set; } = ""; }
                public class Outer { public Inner? Inner { get; set; } }
                public class OuterDto { public Inner? Inner { get; set; } }

                public class OuterProfile : MappingProfile
                {
                    public OuterProfile()
                    {
                        CreateMap<Outer, OuterDto>()
                            .When(s => s.Inner?.Name.Shout() != null);
                    }
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        Assert.Contains("if (!(source.Inner?.Name.Shout() != null)) return default!;", Join(generated));
    }

    [Fact]
    public void CallToOtherGeneratedMapping_NestedObjectWorkaround_Compiles()
    {
        // docs/advanced.md: nested objects are mapped explicitly via a generated MapTo* method.
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>()
                        .ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()));
                }
            }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        // The generator sees the user code before MapToAddressDto exists, so the call stays as written;
        // in the output it binds to the generated method of the same class.
        AssertCompiles(errors);
        Assert.Contains("Address = source.Address.MapToAddressDto()", Join(generated));
    }

    // ------------------------------------------------------------------
    // Static members of other classes without a full name
    // ------------------------------------------------------------------

    [Fact]
    public void StaticMembersOfFrameworkTypes_WithoutFullName_AreQualified()
    {
        string source = """
            using System;
            using System.Linq;
            using IronMapper.Configuration;
            using static System.Math;

            namespace App;

            public class Reading { public decimal Value { get; set; } public int Seconds { get; set; } public int[] Samples { get; set; } = new int[0]; }
            public class ReadingDto { public decimal Rounded { get; set; } public TimeSpan Duration { get; set; } public double Root { get; set; } public int Total { get; set; } }

            public class ReadingProfile : MappingProfile
            {
                public ReadingProfile()
                {
                    CreateMap<Reading, ReadingDto>()
                        .ForMember(d => d.Rounded, o => o.MapFrom(s => Math.Round(s.Value, 2)))
                        .ForMember(d => d.Duration, o => o.MapFrom(s => TimeSpan.FromSeconds(s.Seconds)))
                        .ForMember(d => d.Root, o => o.MapFrom(s => Sqrt(s.Seconds)))
                        .ForMember(d => d.Total, o => o.MapFrom(s => s.Samples.Where(x => x > 0).Sum()));
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.Contains("global::System.Math.Round(source.Value, 2)", code);
        Assert.Contains("global::System.TimeSpan.FromSeconds(source.Seconds)", code);
        Assert.Contains("global::System.Math.Sqrt(source.Seconds)", code);
        Assert.Contains("global::System.Linq.Enumerable.Sum(global::System.Linq.Enumerable.Where(source.Samples, x => x > 0))", code);
    }

    // ------------------------------------------------------------------
    // Constants and enums from another namespace
    // ------------------------------------------------------------------

    [Fact]
    public void ConstantAndEnumFromAnotherNamespace_AreQualified()
    {
        string source = """
            using IronMapper.Configuration;
            using App.Domain;

            namespace App.Domain
            {
                public static class Limits { public const int Max = 10; }
                public enum Level { Low, High }
            }

            namespace App
            {
                public class Stock { public int Count { get; set; } }
                public class StockDto { public Level Level { get; set; } public int Max { get; set; } }

                public class StockProfile : MappingProfile
                {
                    private const int Fallback = 3;
                    internal const int Spare = 1;

                    public StockProfile()
                    {
                        CreateMap<Stock, StockDto>()
                            .ForMember(d => d.Level, o => o.MapFrom(s => s.Count > Limits.Max ? Level.High : Level.Low))
                            .ForMember(d => d.Max, o => o.MapFrom(s => Limits.Max + Spare));
                    }
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.Contains("source.Count > global::App.Domain.Limits.Max ? global::App.Domain.Level.High : global::App.Domain.Level.Low", code);
        Assert.Contains("global::App.Domain.Limits.Max + global::App.StockProfile.Spare", code);
    }

    // ------------------------------------------------------------------
    // Parameter renaming by symbol, not by text
    // ------------------------------------------------------------------

    [Fact]
    public void ParameterNameMatchingMemberNameAndStringLiteral_OnlyParameterIsRenamed()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Letter { public string s { get; set; } = ""; }
            public class LetterDto { public string Text { get; set; } = ""; public string Note { get; set; } = ""; }

            public class LetterProfile : MappingProfile
            {
                public LetterProfile()
                {
                    CreateMap<Letter, LetterDto>()
                        .ForMember(d => d.Text, o => o.MapFrom(s => s.s + "s"))
                        .ForMember(d => d.Note, o => o.MapFrom(s => $"s={s.s}"));
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.Contains("Text = source.s + \"s\"", code);
        Assert.Contains("Note = $\"s={source.s}\"", code);
    }

    // ------------------------------------------------------------------
    // Captured profile state and local variables
    // ------------------------------------------------------------------

    [Fact]
    public void InstanceFieldOfProfile_ReportsIM0012_AndOtherMappingsStillGenerate()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public int Value { get; set; } }
            public class ItemDto { public int Value { get; set; } }
            public class ItemCopy { public int Value { get; set; } }

            public class ItemProfile : MappingProfile
            {
                private readonly int _offset = 5;

                public ItemProfile()
                {
                    CreateMap<Item, ItemDto>()
                        .ForMember(d => d.Value, o => o.MapFrom(s => s.Value + _offset));
                    CreateMap<Item, ItemCopy>();
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0012", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("_offset", diagnostic.GetMessage());
        Assert.Contains("App.ItemProfile", diagnostic.GetMessage());
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.DoesNotContain("MapToItemDto", code);
        Assert.Contains("MapToItemCopy", code);
    }

    [Fact]
    public void NameofProfileInstanceMember_IsEmittedAsLiteral_WithoutDiagnostic()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public int Value { get; set; } }
            public class ItemDto { public string Label { get; set; } = ""; }

            public class ItemProfile : MappingProfile
            {
                public int Counter { get; set; }

                public ItemProfile()
                {
                    CreateMap<Item, ItemDto>()
                        .ForMember(d => d.Label, o => o.MapFrom(s => nameof(Counter) + s.Value));
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        Assert.Contains("Label = \"Counter\" + source.Value", Join(generated));
    }

    [Fact]
    public void InstanceMethodOfProfile_ReportsIM0012()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public int Value { get; set; } }
            public class ItemDto { public int Value { get; set; } }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile()
                {
                    CreateMap<Item, ItemDto>()
                        .ForMember(d => d.Value, o => o.MapFrom(s => Double(s.Value)));
                }

                public int Double(int value) => value * 2;
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0012", diagnostic.Id);
        Assert.Contains("Double", diagnostic.GetMessage());
        AssertCompiles(errors);
    }

    [Fact]
    public void LocalVariableOfConstructor_ReportsIM0012()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public int Value { get; set; } }
            public class ItemDto { public int Value { get; set; } }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile()
                {
                    var factor = 2;
                    CreateMap<Item, ItemDto>()
                        .ForMember(d => d.Value, o => o.MapFrom(s => s.Value * factor));
                }
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0012", diagnostic.Id);
        Assert.Contains("factor", diagnostic.GetMessage());
        Assert.Contains("local variable", diagnostic.GetMessage());
        AssertCompiles(errors);
    }

    // ------------------------------------------------------------------
    // The same rewriting applies to every profile lambda
    // ------------------------------------------------------------------

    [Fact]
    public void WhenConvertUsingHooksAndTransformers_AreRewrittenToo()
    {
        string source = """
            using System;
            using IronMapper.Configuration;
            using App.Domain;

            namespace App.Domain
            {
                public enum State { Draft, Active }
            }

            namespace App
            {
                public class Doc { public State State { get; set; } public decimal Price { get; set; } public string Name { get; set; } = ""; }
                public class DocDto { public decimal Price { get; set; } public string Name { get; set; } = ""; public DateTime Stamp { get; set; } }
                public class DocCard { public string Name { get; set; } = ""; }

                public class DocProfile : MappingProfile
                {
                    public DocProfile()
                    {
                        AddTransformer<decimal>(v => Math.Round(v, 2));
                        CreateMap<Doc, DocDto>()
                            .When(s => s.State == State.Active)
                            .BeforeMap((s, d) => d.Stamp = DateTime.MinValue)
                            .AfterMap((s, d) => { var name = Normalize(s.Name); d.Name = name; });
                        CreateMap<Doc, DocCard>()
                            .ConvertUsing(s => new DocCard { Name = Normalize(s.Name) });
                    }

                    internal static string Normalize(string value) => value.Trim();
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        string code = Join(generated);
        Assert.Contains("source.State == global::App.Domain.State.Active", code);
        Assert.Contains("destination.Stamp = global::System.DateTime.MinValue;", code);
        Assert.Contains("var name = global::App.DocProfile.Normalize(source.Name);", code);
        Assert.Contains("new global::App.DocCard { Name = global::App.DocProfile.Normalize(source.Name) }", code);
        Assert.Contains("global::System.Math.Round(value, 2)", code);
    }

    [Fact]
    public void TransformerUsingPrivateMember_ReportsIM0011()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public string Name { get; set; } = ""; }
            public class ItemDto { public string Name { get; set; } = ""; }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile()
                {
                    AddTransformer<string>(v => Clean(v));
                    CreateMap<Item, ItemDto>();
                }

                private static string Clean(string value) => value.Trim();
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0011", diagnostic.Id);
        Assert.Contains("Clean", diagnostic.GetMessage());
        AssertCompiles(errors);
    }
}
