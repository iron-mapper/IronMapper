using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Converters (<c>[MapConverter]</c>, <c>UseConverter&lt;T&gt;()</c>, <c>ConvertUsing&lt;T&gt;()</c>): which converters the
/// generated code caches (<c>ConverterCache&lt;T&gt;</c> in <c>GeneratedMappers.g.cs</c>) and IM0015 for converters that
/// generated code cannot create. Runtime behavior of the cache: <c>tests/IronMapper.Tests/ConverterCacheTests.cs</c>.
/// </summary>
public class ConverterGeneratorTests
{
    private const string Cache = "ConverterCache<";

    private static string Generated(System.Collections.Generic.IReadOnlyList<string> sources, string fileMarker)
        => sources.Single(s => s.Contains(fileMarker) && !s.Contains("RegisterIronMapperMappings"));

    private static string ClassDeclaration(System.Collections.Generic.IReadOnlyList<string> sources)
        => sources.Single(s => s.Contains("static partial class GeneratedMappers") && s.Contains("/// <summary>Mapping extension methods"));

    [Fact]
    public void StatelessConverters_UseOneCacheDeclaredOnce()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class Upper : ITypeConverter<string, string> { public string Convert(string s) => s.ToUpperInvariant(); }
                public sealed class Whole : ITypeConverter<Price, PriceDto> { public PriceDto Convert(Price p) => new PriceDto { Text = p.Amount.ToString() }; }

                [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(Upper))] public string Label { get; set; } = ""; }
                public class TagDto { public string Label { get; set; } = ""; }
                [MapTo(typeof(NoteDto))] public class Note { [MapConverter(typeof(Upper))] public string Text { get; set; } = ""; }
                public class NoteDto { public string Text { get; set; } = ""; }
                public class Item { public string Code { get; set; } = ""; }
                public class ItemDto { public string Code { get; set; } = ""; }
                public class Price { public decimal Amount { get; set; } }
                public class PriceDto { public string Text { get; set; } = ""; }

                public class Profile1 : MappingProfile
                {
                    public Profile1()
                    {
                        CreateMap<Item, ItemDto>().ForMember(d => d.Code, o => o.UseConverter<Upper>());
                        CreateMap<Price, PriceDto>().ConvertUsing<Whole>();
                    }
                }

                public static class Program
                {
                    public static string Run()
                        => new Tag { Label = "a" }.MapToTagDto().Label + new Note { Text = "b" }.MapToNoteDto().Text
                           + new Item { Code = "c" }.MapToItemDto().Code + new Price { Amount = 4 }.MapToPriceDto().Text;
                }
            }
            """;

        var (sources, generatorDiagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        Assert.Empty(errors);
        Assert.Empty(generatorDiagnostics);
        Assert.Equal("ABC4", result);

        // One holder in GeneratedMappers.g.cs, none in the per-mapping files.
        Assert.Single(sources, s => s.Contains("class ConverterCache<"));
        Assert.Contains("class ConverterCache<", ClassDeclaration(sources));

        Assert.Contains("ConverterCache<global::App.Upper>.Instance.Convert(source.Label)", Generated(sources, "MapToTagDto("));
        Assert.Contains("ConverterCache<global::App.Upper>.Instance.Convert(source.Text)", Generated(sources, "MapToNoteDto("));
        Assert.Contains("ConverterCache<global::App.Upper>.Instance.Convert(source.Code)", Generated(sources, "MapToItemDto("));
        Assert.Contains("return ConverterCache<global::App.Whole>.Instance.Convert(source);", Generated(sources, "MapToPriceDto("));
        Assert.DoesNotContain("().Convert(", string.Join("\n", sources));
    }

    [Fact]
    public void NoCachedConverter_NoCacheDeclared()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class Counting : ITypeConverter<string, string>
                {
                    private int _calls;
                    public string Convert(string s) => s + (++_calls);
                }

                [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(Counting))] public string Label { get; set; } = ""; }
                public class TagDto { public string Label { get; set; } = ""; }
                [MapTo(typeof(PlainDto))] public class Plain { public int Id { get; set; } }
                public class PlainDto { public int Id { get; set; } }
            }
            """;

        var (sources, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        Assert.DoesNotContain(sources, s => s.Contains(Cache));
        Assert.Contains("new global::App.Counting().Convert(source.Label)", Generated(sources, "MapToTagDto("));
    }

    [Theory]
    // Instance state anywhere in the hierarchy (fields, auto-property backing fields, field-like events, captured
    // primary constructor parameters) or code in a constructor: new instance per call, as before.
    [InlineData("public sealed class C : ITypeConverter<string, string> { private int _n; public string Convert(string s) => s + _n++; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public int N { get; set; } public string Convert(string s) => s + N++; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public event System.EventHandler? Converted; public string Convert(string s) { Converted?.Invoke(this, System.EventArgs.Empty); return s; } }")]
    [InlineData("public class B { protected int N; } public sealed class C : B, ITypeConverter<string, string> { public string Convert(string s) => s + N++; }")]
    [InlineData("public class A { private readonly object _lock = new(); } public class B : A { } public sealed class C : B, ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public static int Created; public C() { Created++; } public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public C() => System.Console.WriteLine(); public string Convert(string s) => s; }")]
    [InlineData("public class B { public B() { System.Console.WriteLine(); } } public sealed class C : B, ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public class B(string prefix) { protected string P => prefix; } public sealed class C() : B(\"x\"), ITypeConverter<string, string> { public string Convert(string s) => P + s; }")]
    // A base class from another assembly: its private fields are not visible to the generator.
    [InlineData("public sealed class C : System.Collections.ObjectModel.Collection<int>, ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    // Not a reference type with a public parameterless constructor (ConverterCache<T> requires class, new()).
    [InlineData("public struct C : ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { internal C() { } public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public C(int start = 0) { } public string Convert(string s) => s; }")]
    public void ConverterThatMayHaveState_NewInstancePerCall(string converter)
    {
        var source = $$"""
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                {{converter}}

                [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(C))] public string Label { get; set; } = ""; }
                public class TagDto { public string Label { get; set; } = ""; }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        Assert.Empty(generatorDiagnostics);
        Assert.Contains("new global::App.C().Convert(source.Label)", Generated(sources, "MapToTagDto("));
        Assert.DoesNotContain(sources, s => s.Contains(Cache));
    }

    [Theory]
    // No fields anywhere and constructors without code: cached.
    [InlineData("public sealed class C : ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public C() { } public string Convert(string s) => s; }")]
    [InlineData("public sealed class C() : ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public static int Shared; private const int K = 1; public int Length => K; public string Convert(string s) => s + Shared; }")]
    [InlineData("public abstract class B { protected B() { } protected static string F(string s) => s; } public sealed class C : B, ITypeConverter<string, string> { public string Convert(string s) => F(s); }")]
    [InlineData("public sealed record C : ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("internal sealed class C : ITypeConverter<string, string> { public string Convert(string s) => s; }")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public string Convert(string s) => s; } public partial class Unused { private int _n; }")]
    public void ConverterWithoutState_Cached(string converter)
    {
        var source = $$"""
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                {{converter}}

                [MapTo(typeof(TagDto))] internal class Tag { [MapConverter(typeof(C))] public string Label { get; set; } = ""; }
                internal class TagDto { public string Label { get; set; } = ""; }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        Assert.Empty(generatorDiagnostics);
        Assert.Contains("ConverterCache<global::App.C>.Instance.Convert(source.Label)", Generated(sources, "MapToTagDto("));
    }

    [Fact]
    public void GenericConverter_CachedPerConstructedType()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class Echo<T> : ITypeConverter<T, T> { public T Convert(T value) => value; }
                public sealed class Holder<T> : ITypeConverter<T, T> { private T? _last; public T Convert(T value) => _last = value; }

                [MapTo(typeof(TagDto))]
                public class Tag
                {
                    [MapConverter(typeof(Echo<string>))] public string Label { get; set; } = "";
                    [MapConverter(typeof(Holder<int>))] public int Count { get; set; }
                }
                public class TagDto { public string Label { get; set; } = ""; public int Count { get; set; } }
            }
            """;

        var (sources, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        var code = Generated(sources, "MapToTagDto(");
        Assert.Contains("ConverterCache<global::App.Echo<string>>.Instance.Convert(source.Label)", code);
        Assert.Contains("new global::App.Holder<int>().Convert(source.Count)", code);
    }

    // -----------------------------------------------------------------------
    // IM0015 — converter cannot be created by generated code
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("public sealed class C : ITypeConverter<string, string> { public C(int start) { } public string Convert(string s) => s; }", "it has no parameterless constructor")]
    [InlineData("public sealed class C(IFormatProvider provider) : ITypeConverter<string, string> { public string Convert(string s) => string.Format(provider, \"{0}\", s); }", "it has no parameterless constructor")]
    [InlineData("public sealed class C : ITypeConverter<string, string> { private C() { } public string Convert(string s) => s; }", "its parameterless constructor is private")]
    [InlineData("public class C : ITypeConverter<string, string> { protected C() { } public string Convert(string s) => s; }", "its parameterless constructor is protected")]
    [InlineData("public abstract class C : ITypeConverter<string, string> { public string Convert(string s) => s; }", "it is abstract")]
    [InlineData("public interface C : ITypeConverter<string, string> { }", "it is an interface")]
    [InlineData("public static class C { }", "it is static")]
    public void ConverterThatCannotBeCreated_IM0015_MappingSkipped_OthersGenerated(string converter, string reason)
    {
        var source = $$"""
            using System;
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                {{converter}}

                [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(C))] public string Label { get; set; } = ""; }
                public class TagDto { public string Label { get; set; } = ""; }
                [MapTo(typeof(OkDto))] public class Ok { public int Id { get; set; } }
                public class OkDto { public int Id { get; set; } }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        // Only IM0015, no CS7036/CS1729/CS0122/CS0144 inside generated code.
        Assert.Empty(errors);
        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0015", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'App.C'", diagnostic.GetMessage());
        Assert.Contains("[MapConverter] on 'App.Tag.Label'", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.Contains("'App.Tag' to 'App.TagDto' was not generated", diagnostic.GetMessage());

        Assert.DoesNotContain(sources, s => s.Contains("MapToTagDto("));
        Assert.Contains(sources, s => s.Contains("MapToOkDto("));
    }

    [Fact]
    public void OpenGenericConverter_IM0015()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class Echo<T> : ITypeConverter<T, T> { public T Convert(T value) => value; }

                [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(Echo<>))] public string Label { get; set; } = ""; }
                public class TagDto { public string Label { get; set; } = ""; }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0015", diagnostic.Id);
        Assert.Contains("because it is an open generic type", diagnostic.GetMessage());
        Assert.DoesNotContain(sources, s => s.Contains("MapToTagDto("));
    }

    [Fact]
    public void PrivateNestedConverter_IM0015_InsteadOfCS0122InGeneratedCode()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                [MapTo(typeof(TagDto))]
                public class Tag
                {
                    [MapConverter(typeof(Upper))] public string Label { get; set; } = "";

                    private sealed class Upper : ITypeConverter<string, string> { public string Convert(string s) => s.ToUpperInvariant(); }
                }
                public class TagDto { public string Label { get; set; } = ""; }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0015", diagnostic.Id);
        Assert.Contains("converter 'App.Tag.Upper' ([MapConverter] on 'App.Tag.Label')", diagnostic.GetMessage());
        Assert.Contains("it is not accessible from the generated class in namespace IronMapper.Generated (private)", diagnostic.GetMessage());
        Assert.DoesNotContain(sources, s => s.Contains("MapToTagDto("));
    }

    [Fact]
    public void ProfileConverterThatCannotBeCreated_IM0015_ForUseConverterAndConvertUsing()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class NeedsArg : ITypeConverter<string, string> { public NeedsArg(string prefix) { } public string Convert(string s) => s; }
                public sealed class WholeNeedsArg : ITypeConverter<Price, PriceDto> { public WholeNeedsArg(int scale) { } public PriceDto Convert(Price p) => new(); }

                public class Item { public string Code { get; set; } = ""; }
                public class ItemDto { public string Code { get; set; } = ""; }
                public class Price { public decimal Amount { get; set; } }
                public class PriceDto { public string Text { get; set; } = ""; }
                public class Ok { public int Id { get; set; } }
                public class OkDto { public int Id { get; set; } }

                public class ConverterProfile : MappingProfile
                {
                    public ConverterProfile()
                    {
                        CreateMap<Item, ItemDto>().ForMember(d => d.Code, o => o.UseConverter<NeedsArg>());
                        CreateMap<Price, PriceDto>().ConvertUsing<WholeNeedsArg>();
                        CreateMap<Ok, OkDto>();
                    }
                }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        var messages = generatorDiagnostics.Where(d => d.Id == "IM0015").Select(d => d.GetMessage()).OrderBy(m => m).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(generatorDiagnostics.Count, messages.Count);
        Assert.Contains("'App.Item' to 'App.ItemDto' was not generated: converter 'App.NeedsArg' (UseConverter for 'Code' in MappingProfile 'App.ConverterProfile')", messages[0]);
        Assert.Contains("'App.Price' to 'App.PriceDto' was not generated: converter 'App.WholeNeedsArg' (ConvertUsing in MappingProfile 'App.ConverterProfile')", messages[1]);

        Assert.DoesNotContain(sources, s => s.Contains("MapToItemDto("));
        Assert.DoesNotContain(sources, s => s.Contains("MapToPriceDto("));
        Assert.Contains(sources, s => s.Contains("MapToOkDto("));
    }

    [Fact]
    public void IgnoredPropertyWithConverterThatCannotBeCreated_NoDiagnostic()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App
            {
                public sealed class NeedsArg : ITypeConverter<string, string> { public NeedsArg(string prefix) { } public string Convert(string s) => s; }

                [MapTo(typeof(TagDto))] public class Tag { [Ignore, MapConverter(typeof(NeedsArg))] public string Label { get; set; } = ""; public int Id { get; set; } }
                public class TagDto { public string Label { get; set; } = ""; public int Id { get; set; } }
            }
            """;

        var (sources, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        Assert.Empty(generatorDiagnostics);
        Assert.Contains(sources, s => s.Contains("MapToTagDto("));
    }
}
