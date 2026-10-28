using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Configuration of a profile that up to 2.0.0 was dropped without a diagnostic (PR 8 of 2.1.0, plan items 2.5 and 2.6):
/// <c>MapFrom</c> with a method group (issue #59) — the member was mapped by name, or the generated file did not compile
/// (<c>CS1061</c>); <c>When</c>, <c>BeforeMap</c>, <c>AfterMap</c> with a method group — no condition, no hook;
/// <c>[Ignore]</c> on a destination property (issue #61) — only its IM0001 was skipped, the source property of the same name
/// was still copied; <c>[Ignore]</c> on a source property — it was still read.
/// </summary>
public class SilentlyIgnoredConfigTests
{
    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    // Line endings of the generated code (Environment.NewLine) and of this file may differ.
    private static string Generated(IEnumerable<string> sources, string mapping)
        => sources.Single(s => s.Contains($" global::App.{mapping} MapTo{mapping}(this ")).ReplaceLineEndings("\n");

    private const string Usings = """
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        """;

    // -----------------------------------------------------------------------
    // MapFrom with a method group (#59)
    // -----------------------------------------------------------------------

    [Fact]
    public void MapFrom_StaticMethodGroup_CalledWithTheSource_NewAndInPlace()
    {
        // Title: the source has a property of the same name (2.0.0: Title = source.Title, Compute never called);
        // Label: it has none (2.0.0: CS1061 in the generated file).
        string source = Usings + """
            public class Row { public string First { get; set; } = ""; public string Title { get; set; } = ""; }
            public class Dto { public string Title { get; set; } = ""; public string Label { get; set; } = ""; }

            public static class Helper { public static string Compute(Row r) => r.First + "!"; }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>()
                        .ForMember(d => d.Title, o => o.MapFrom(Helper.Compute))
                        .ForMember(d => d.Label, o => o.MapFrom(Helper.Compute));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new Row { First = "a", Title = "copied" };
                    var dto = row.MapToDto();
                    var existing = new Dto();
                    row.MapToDto(existing);
                    return $"{dto.Title},{dto.Label},{existing.Title},{existing.Label}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("a!,a!,a!,a!", run);
        string code = Generated(generated, "Dto");
        Assert.Contains("Title = global::App.Helper.Compute(source),", code);
        Assert.Contains("Label = global::App.Helper.Compute(source),", code);
        Assert.Contains("destination.Title = global::App.Helper.Compute(source);", code);
        Assert.DoesNotContain("source.Title", code);
    }

    [Theory]
    [InlineData("new System.Func<Row, object?>(Helper.Compute)", "global::App.Helper.Compute(source)")]
    [InlineData("Helper.Generic<Row>", "global::App.Helper.Generic<global::App.Row>(source)")]
    [InlineData("Helper.FromBase", "global::App.Helper.FromBase(source)")]
    [InlineData("Helper.Extension", "global::App.Helper.Extension(source)")]
    [InlineData("Internal.Compute", "global::App.Internal.Compute(source)")]
    public void MapFrom_OtherFormsOfAStaticMethod_Called(string argument, string call)
    {
        string source = Usings + """
            public class Named { public string First { get; set; } = ""; }
            public class Row : Named { }
            public class Dto { public string Title { get; set; } = ""; }

            public static class Helper
            {
                public static string Compute(Row r) => r.First + "!";
                public static string Generic<T>(T r) where T : Named => r.First + "!";
                public static string FromBase(Named r) => r.First + "!";
                public static string Extension(this Row r) => r.First + "!";
            }

            internal static class Internal { internal static string Compute(Row r) => r.First + "!"; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().ForMember(d => d.Title, o => o.MapFrom(ARG)); }
            }

            public static class Program
            {
                public static string Run() => new Row { First = "a" }.MapToDto().Title;
            }
            """.Replace("ARG", argument);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("a!", run);
        Assert.Contains($"Title = {call},", generated.Single(s => s.Contains("MapToDto(this ")).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("new Instance().Compute", "'Instance.Compute' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.")]
    [InlineData("Local", "'Local' is a local function, which the generated class cannot call. Declare it as a static method of a type, or pass a lambda.")]
    [InlineData("Hidden", "'RowProfile.Hidden' cannot be called by the generated class: 'App.RowProfile.Hidden(App.Row)' is private. Make it public or internal.")]
    [InlineData("stored", "MapFrom takes a lambda, such as MapFrom(s => …), or a static method group, such as MapFrom(Helper.Compute), written in the call; 'stored' is neither, so the generated method cannot call it.")]
    [InlineData("Stored", "MapFrom takes a lambda, such as MapFrom(s => …), or a static method group, such as MapFrom(Helper.Compute), written in the call; 'Stored' is neither, so the generated method cannot call it.")]
    [InlineData("delegate (Row r) { return r.First; }", "MapFrom takes a lambda, such as MapFrom(s => …), or a static method group, such as MapFrom(Helper.Compute), written in the call; 'delegate (Row r) { return r.First; }' is neither, so the generated method cannot call it.")]
    public void MapFrom_ArgumentTheGeneratedCodeCannotCall_IM0024AtTheArgument_MappingNotGenerated(string argument, string reason)
    {
        // Label has no source property of the same name: 2.0.0 generated source.Label (CS1061 in the generated file).
        string source = Usings + """
            public class Row { public string First { get; set; } = ""; }
            public class Dto { public string Label { get; set; } = ""; }

            public class Instance { public string Compute(Row r) => r.First; }

            public class RowProfile : MappingProfile
            {
                private static readonly System.Func<Row, object?> Stored = r => r.First;

                public RowProfile()
                {
                    System.Func<Row, object?> stored = r => r.First;
                    string Local(Row r) => r.First;
                    CreateMap<Row, Dto>().ForMember(d => d.Label, o => o.MapFrom(ARG));
                }

                private static string Hidden(Row r) => r.First;
            }
            """.Replace("ARG", argument);

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0024 = Assert.Single(diagnostics);
        Assert.Equal("IM0024", im0024.Id);
        Assert.Equal(DiagnosticSeverity.Error, im0024.Severity);
        Assert.Equal($"Mapping from 'App.Row' to 'App.Dto' was not generated: MapFrom for 'Label' cannot be used: {reason}", Message(im0024));
        Assert.Equal(argument, source.Substring(im0024.Location.SourceSpan.Start, im0024.Location.SourceSpan.Length));
        Assert.DoesNotContain(generated, s => s.Contains("MapToDto("));
    }

    [Fact]
    public void MapFrom_MethodGroupInForCtorParam_CalledForTheParameter_IM0024NamesTheParameter()
    {
        string source = Usings + """
            public class Row { public string First { get; set; } = ""; }
            public class Card
            {
                public Card(string title, string owner) { Title = title; Owner = owner; }
                public string Title { get; }
                public string Owner { get; }
            }
            public class Other
            {
                public Other(string owner) => Owner = owner;
                public string Owner { get; }
            }

            public static class Helper { public static string Owner(Row r) => r.First + "@"; }
            public class Instance { public string Owner(Row r) => r.First; }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Card>()
                        .ForCtorParam("title", o => o.MapFrom(s => s.First))
                        .ForCtorParam("owner", o => o.MapFrom(Helper.Owner));
                    CreateMap<Row, Other>().ForCtorParam("owner", o => o.MapFrom(new Instance().Owner));
                }
            }

            public static class Program
            {
                public static string Run() { var card = new Row { First = "a" }.MapToCard(); return $"{card.Title},{card.Owner}"; }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("a,a@", run);
        Assert.Contains("owner: global::App.Helper.Owner(source)", Generated(generated, "Card"));
        var im0024 = Assert.Single(diagnostics);
        Assert.Equal("IM0024", im0024.Id);
        Assert.Equal("Mapping from 'App.Row' to 'App.Other' was not generated: MapFrom for 'owner' cannot be used: 'Instance.Owner' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.",
            Message(im0024));
        Assert.DoesNotContain(generated, s => s.Contains("MapToOther("));
    }

    [Fact]
    public void MapFrom_MethodGroup_ReturnTypeCheckedAsTheLambda_IM0002AndIM0023AtTheArgument()
    {
        // The return type is the type of the value: object cannot be assigned to string (IM0002, the property is left
        // out); string? to string can receive null (IM0023, mapped with !).
        string source = Usings + """
            public class Row { public string First { get; set; } = ""; }
            public class Dto { public string Title { get; set; } = ""; public string Label { get; set; } = ""; }

            public static class Helper
            {
                public static object Boxed(Row r) => r.First;
                public static string? Maybe(Row r) => r.First.Length == 0 ? null : r.First;
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>()
                        .ForMember(d => d.Title, o => o.MapFrom(Helper.Boxed))
                        .ForMember(d => d.Label, o => o.MapFrom(Helper.Maybe));
                }
            }

            public static class Program
            {
                public static string Run() { var dto = new Row { First = "a" }.MapToDto(); return $"[{dto.Title}],[{dto.Label}]"; }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("[],[a]", run);
        Assert.Equal(new[] { "IM0002", "IM0023" }, diagnostics.Select(d => d.Id).OrderBy(id => id).ToArray());
        Assert.Equal("Helper.Boxed", source.Substring(diagnostics.Single(d => d.Id == "IM0002").Location.SourceSpan.Start, "Helper.Boxed".Length));
        Assert.Equal("Helper.Maybe", source.Substring(diagnostics.Single(d => d.Id == "IM0023").Location.SourceSpan.Start, "Helper.Maybe".Length));
        string code = Generated(generated, "Dto");
        Assert.Contains("Label = (global::App.Helper.Maybe(source))!,", code);
        Assert.DoesNotContain("Boxed", code);
    }

    // -----------------------------------------------------------------------
    // [Ignore] on a destination property of a profile mapping (#61)
    // -----------------------------------------------------------------------

    [Fact]
    public void Ignore_OnDestinationProperty_NotCopied_NewAndInPlace()
    {
        string source = Usings + """
            public class Row { public string Title { get; set; } = ""; public string Secret { get; set; } = ""; }
            public class Dto { public string Title { get; set; } = ""; [Ignore] public string Secret { get; set; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new Row { Title = "t", Secret = "s" };
                    var dto = row.MapToDto();
                    var existing = new Dto { Secret = "kept" };
                    row.MapToDto(existing);
                    return $"{dto.Title},[{dto.Secret}],{existing.Title},{existing.Secret}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source,
            new Dictionary<string, string> { ["IronMapperReportUnmappedInProfiles"] = "true" });

        AssertNoErrors(errors);
        // [Ignore] still counts as mapped: no IM0001 with IronMapperReportUnmappedInProfiles.
        Assert.Empty(diagnostics);
        Assert.Equal("t,[],t,kept", run);
        Assert.DoesNotContain("Secret", Generated(generated, "Dto"));
        // IMapper's in-place update (the registration) is the same method.
        Assert.DoesNotContain(generated, s => s.Contains("Secret"));
    }

    [Theory]
    [InlineData("o => o.MapFrom(s => s.Secret + \"?\")", "s?")]
    [InlineData("o => o.MapFrom(Helper.Secret)", "s!")]
    public void Ignore_ForMemberOfTheSamePropertyWins(string options, string expected)
    {
        string source = Usings + """
            public class Row { public string Secret { get; set; } = ""; }
            public class Dto { [Ignore] public string Secret { get; set; } = ""; }

            public static class Helper { public static string Secret(Row r) => r.Secret + "!"; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().ForMember(d => d.Secret, OPTIONS); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new Row { Secret = "s" };
                    var existing = new Dto();
                    row.MapToDto(existing);
                    return $"{row.MapToDto().Secret},{existing.Secret}";
                }
            }
            """.Replace("OPTIONS", options);

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal($"{expected},{expected}", run);
    }

    [Fact]
    public void Ignore_ReverseMap_OnEitherTypeLeavesThePropertyOutOfBothDirections()
    {
        // As in attribute mappings: [Ignore] on a destination property is not set, on a source property not read.
        string source = Usings + """
            public class Row { public string Title { get; set; } = ""; public string Secret { get; set; } = ""; [Ignore] public string Note { get; set; } = ""; }
            public class Dto { public string Title { get; set; } = ""; [Ignore] public string Secret { get; set; } = ""; public string Note { get; set; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().ReverseMap(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Row { Title = "t", Secret = "s", Note = "n" }.MapToDto();
                    var row = new Dto { Title = "t2", Secret = "s2", Note = "n2" }.MapToRow();
                    return $"{dto.Title},[{dto.Secret}],[{dto.Note}],{row.Title},[{row.Secret}],[{row.Note}]";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("t,[],[],t2,[],[]", run);
        Assert.DoesNotContain(generated, s => s.Contains("Secret") || s.Contains("Note"));
    }

    [Fact]
    public void Ignore_OnSourceProperty_NotRead_NewAndInPlace_ForMemberWins_IncludeMembers()
    {
        // Secret: [Ignore] on the source property (2.0.0: copied). Code: ForMember reads it explicitly and wins.
        // City: [Ignore] on the property of an included member (2.0.0: copied).
        string source = Usings + """
            public class Address { [Ignore] public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
            public class Row
            {
                public string Title { get; set; } = "";
                [Ignore] public string Secret { get; set; } = "";
                [Ignore] public string Code { get; set; } = "";
                public Address Address { get; set; } = new();
            }
            public class Dto
            {
                public string Title { get; set; } = "";
                public string Secret { get; set; } = "";
                public string Code { get; set; } = "";
                public string City { get; set; } = "";
                public string Zip { get; set; } = "";
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>().ForMember(d => d.Code, o => o.MapFrom(s => s.Code + "?")).IncludeMembers(s => s.Address);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new Row { Title = "t", Secret = "s", Code = "c", Address = new Address { City = "x", Zip = "z" } };
                    var dto = row.MapToDto();
                    var existing = new Dto { Secret = "kept", City = "kept" };
                    row.MapToDto(existing);
                    return $"{dto.Title},[{dto.Secret}],{dto.Code},[{dto.City}],{dto.Zip},{existing.Secret},{existing.City},{existing.Code}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source,
            new Dictionary<string, string> { ["IronMapperReportUnmappedInProfiles"] = "true" });

        AssertNoErrors(errors);
        Assert.Equal("t,[],c?,[],z,kept,kept,c?", run);
        // An ignored source property counts as mapped (as in attribute mappings); City has no source left: IM0001.
        var im0001 = Assert.Single(diagnostics);
        Assert.Equal("IM0001", im0001.Id);
        Assert.Contains("'City'", Message(im0001));
        Assert.DoesNotContain("Secret", Generated(generated, "Dto"));
    }

    [Fact]
    public void Ignore_OnSourceProperty_ConstructorParameterGetsItsDefaultValue()
    {
        string source = Usings + """
            public class Row { public string Title { get; set; } = ""; [Ignore] public string Secret { get; set; } = ""; }
            public record Card(string Title, string Secret = "none");
            public record Strict(string Title, string Secret);

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Card>();
                    CreateMap<Row, Strict>();
                }
            }

            public static class Program
            {
                public static string Run() => new Row { Title = "t", Secret = "s" }.MapToCard().Secret;
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("none", run);
        // No default value: IM0009 names the [Ignore], as in attribute mappings.
        var im0009 = Assert.Single(diagnostics);
        Assert.Equal("IM0009", im0009.Id);
        Assert.Contains("[Ignore] on 'Row.Secret'", Message(im0009));
        Assert.DoesNotContain(generated, s => s.Contains("MapToStrict("));
    }

    // -----------------------------------------------------------------------
    // When, BeforeMap, AfterMap with a method group
    // -----------------------------------------------------------------------

    [Fact]
    public void WhenAndHooks_StaticMethodGroups_Called()
    {
        // 2.0.0: no condition (an inactive row was mapped), neither hook called.
        string source = Usings + """
            public class Row { public bool Active { get; set; } public string First { get; set; } = ""; }
            public class Dto { public string First { get; set; } = ""; public string Log { get; set; } = ""; }

            public static class Rules { public static bool IsActive(Row r) => r.Active; }
            public static class Audit
            {
                public static void Before(Row r, Dto d) => d.Log += "before;";
                public static void After(Row r, Dto d) => d.Log += "after:" + d.First;
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().When(Rules.IsActive).BeforeMap(Audit.Before).AfterMap(Audit.After); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var active = new Row { Active = true, First = "a" }.MapToDto();
                    var inactive = new Row { First = "b" }.MapToDto();
                    return $"{active.Log},{inactive is null}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("before;after:a,True", run);
        string code = Generated(generated, "Dto");
        Assert.Contains("if (!(global::App.Rules.IsActive(source))) return default!;", code);
        Assert.Contains("global::App.Audit.Before(source, destination);", code);
        Assert.Contains("global::App.Audit.After(source, destination);", code);
    }

    [Theory]
    [InlineData("When(new Instance().IsActive)", "When", "'Instance.IsActive' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.")]
    [InlineData("When(Hidden)", "When", "'RowProfile.Hidden' cannot be called by the generated class: 'App.RowProfile.Hidden(App.Row)' is private. Make it public or internal.")]
    [InlineData("When(condition)", "When", "When takes a lambda, such as When(s => …), or a static method group, such as When(Rules.IsActive), written in the call; 'condition' is neither, so the generated method cannot call it.")]
    [InlineData("BeforeMap(Local)", "BeforeMap", "'Local' is a local function, which the generated class cannot call. Declare it as a static method of a type, or pass a lambda.")]
    [InlineData("AfterMap(Stored)", "AfterMap", "AfterMap takes a lambda, such as AfterMap((s, d) => …), or a static method group, such as AfterMap(Audit.Stamp), written in the call; 'Stored' is neither, so the generated method cannot call it.")]
    [InlineData("AfterMap(new Instance().Stamp)", "AfterMap", "'Instance.Stamp' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.")]
    public void WhenAndHooks_ArgumentTheGeneratedCodeCannotCall_IM0024AtTheArgument_MappingNotGenerated(string call, string what, string reason)
    {
        string source = Usings + """
            public class Row { public string First { get; set; } = ""; }
            public class Dto { public string First { get; set; } = ""; }

            public class Instance
            {
                public bool IsActive(Row r) => true;
                public void Stamp(Row r, Dto d) { }
            }

            public class RowProfile : MappingProfile
            {
                private static readonly System.Action<Row, Dto> Stored = (r, d) => { };

                public RowProfile()
                {
                    System.Func<Row, bool> condition = r => true;
                    void Local(Row r, Dto d) { }
                    CreateMap<Row, Dto>().CALL;
                }

                private static bool Hidden(Row r) => true;
            }
            """.Replace("CALL", call);

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0024 = Assert.Single(diagnostics);
        Assert.Equal("IM0024", im0024.Id);
        Assert.Equal($"Mapping from 'App.Row' to 'App.Dto' was not generated: {what} cannot be used: {reason}", Message(im0024));
        string argument = call.Substring(what.Length + 1, call.Length - what.Length - 2);
        Assert.Equal(argument, source.Substring(im0024.Location.SourceSpan.Start, im0024.Location.SourceSpan.Length));
        Assert.DoesNotContain(generated, s => s.Contains("MapToDto("));
    }

    [Fact]
    public void Ignore_OnThePropertyOfAConstructorParameter_DefaultValue_ForMemberWins()
    {
        // As in attribute mappings: [Ignore] on the property of the same name leaves the parameter its default value.
        string source = Usings + """
            public class Row { public string Title { get; set; } = ""; public string Secret { get; set; } = ""; }
            public record Card(string Title, [property: Ignore] string Secret = "none");
            public record Mapped(string Title, [property: Ignore] string Secret = "none");

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Card>();
                    CreateMap<Row, Mapped>().ForMember(d => d.Secret, o => o.MapFrom(s => s.Secret + "?"));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new Row { Title = "t", Secret = "s" };
                    return $"{row.MapToCard().Secret},{row.MapToMapped().Secret}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("none,s?", run);
    }
}
