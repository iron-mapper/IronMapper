using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Diagnostics after an edit in the IDE: the generator driver keeps running on the same state, so a mapping whose
/// diagnostics changed while everything else stayed the same must not be treated as unchanged (cached). Otherwise a
/// fixed problem keeps its warning, or a new problem is not reported, until the IDE restarts the generator.
/// Each test runs the driver on the first version, then on the edited one, and compares the second run with a
/// fresh run on the edited version.
/// </summary>
public class IncrementalDiagnosticsTests
{
    private const string AttributeMappingWithUnmatchedProperty = """
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(UserDto))]
        public class User { public int Id { get; set; } }

        public class UserDto
        {
            public int Id { get; set; }
            /*IGNORE*/public string Nickname { get; set; } = "";
        }
        """;

    private const string ProfileWithTwoPrivateMembers = """
        using IronMapper.Configuration;

        namespace App;

        public class Shift { public int Minutes { get; set; } }
        public class ShiftDto { public string Text { get; set; } = ""; }

        public class ShiftProfile : MappingProfile
        {
            public ShiftProfile()
            {
                CreateMap<Shift, ShiftDto>()
                    .ForMember(d => d.Text, o => o.MapFrom(s => Prefix() + Format(s.Minutes)));
            }

            /*PREFIX*/private static string Prefix() => "Shift ";
            /*FORMAT*/private static string Format(int minutes) => minutes + " min";
        }
        """;

    [Fact]
    public void AttributeMapping_IM0001_DisappearsWhenPropertyIgnored()
    {
        string before = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "");
        string after = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "[Ignore] ");

        string[] ids = AssertSameAsFreshRun(before, after);
        Assert.DoesNotContain("IM0001", ids);
    }

    [Fact]
    public void AttributeMapping_IM0001_AppearsWhenIgnoreRemoved()
    {
        string before = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "[Ignore] ");
        string after = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "");

        string[] ids = AssertSameAsFreshRun(before, after);
        Assert.Contains("IM0001", ids);
    }

    [Fact]
    public void ProfileLambda_OneOfTwoIM0011_DisappearsWhenMemberMadeInternal()
    {
        string before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");
        string after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/", "");

        string[] ids = AssertSameAsFreshRun(before, after);
        Assert.Equal(new[] { "IM0011" }, ids);
    }

    [Fact]
    public void ProfileLambda_SecondIM0011_AppearsWhenMemberMadePrivate()
    {
        string before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/", "");
        string after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");

        string[] ids = AssertSameAsFreshRun(before, after);
        Assert.Equal(new[] { "IM0011", "IM0011" }, ids);
    }

    [Fact]
    public void ProfileLambda_IM0011_DisappearsWhenAllMembersMadeInternal()
    {
        string before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");
        string after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/private", "internal");

        string[] ids = AssertSameAsFreshRun(before, after);
        Assert.Empty(ids);
    }

    // The converter is declared in another file than the mapping: editing only that file must reach the mapping's output.
    private const string MappingWithConverter = """
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(TagDto))]
        public class Tag { [MapConverter(typeof(Upper))] public string Label { get; set; } = ""; }
        public class TagDto { public string Label { get; set; } = ""; }
        """;

    private const string Converter = """
        using IronMapper.Interfaces;

        namespace App;

        public sealed class Upper : ITypeConverter<string, string>
        {
            /*MEMBER*/
            public string Convert(string source) => source.ToUpperInvariant();
        }
        """;

    [Fact]
    public void Converter_FieldAddedInAnotherFile_CacheReplacedByNew()
    {
        string[] sources = AssertSameAsFreshRun(
            new[] { MappingWithConverter, Converter }, 1, Converter.Replace("/*MEMBER*/", "private int _calls;"), out string[]? ids);

        Assert.Empty(ids);
        Assert.Contains(sources, s => s.Contains("new global::App.Upper().Convert(source.Label)"));
        Assert.DoesNotContain(sources, s => s.Contains("ConverterCache<"));
    }

    [Fact]
    public void Converter_FieldRemovedInAnotherFile_Cached()
    {
        string[] sources = AssertSameAsFreshRun(
            new[] { MappingWithConverter, Converter.Replace("/*MEMBER*/", "private int _calls;") }, 1, Converter, out string[]? ids);

        Assert.Empty(ids);
        Assert.Contains(sources, s => s.Contains("ConverterCache<global::App.Upper>.Instance.Convert(source.Label)"));
    }

    [Fact]
    public void Converter_IM0015_AppearsAndDisappearsWithConstructor()
    {
        string withArgument = Converter.Replace("/*MEMBER*/", "public Upper(int start) { }");

        AssertSameAsFreshRun(new[] { MappingWithConverter, Converter }, 1, withArgument, out string[]? appeared);
        Assert.Equal(new[] { "IM0015" }, appeared);

        string[] sources = AssertSameAsFreshRun(new[] { MappingWithConverter, withArgument }, 1, Converter, out string[]? disappeared);
        Assert.Empty(disappeared);
        Assert.Contains(sources, s => s.Contains("MapToTagDto(this"));
    }

    // A value converted by the mapping of its pair (Guid → Ref), declared in another file than the mapping that uses it.
    private const string MappingWithValueObject = """
        using IronMapper.Configuration;

        namespace App;

        public readonly record struct Ref
        {
            private Ref(System.Guid value) => Value = value;
            public System.Guid Value { get; }
            public static Ref From(System.Guid value) => new(value);
        }
        public class Link { public System.Guid Target { get; set; } public string Name { get; set; } = ""; }
        public class LinkDto { public Ref Target { get; set; } public string Name { get; set; } = ""; }
        public class LinkProfile : MappingProfile { public LinkProfile() { CreateMap<Link, LinkDto>(); } }
        """;

    private const string NoPairMapping = """
        namespace App;

        public static class Nothing { }
        """;

    private const string PairMapping = """
        using IronMapper.Configuration;

        namespace App;

        public class RefProfile : MappingProfile { public RefProfile() { CreateMap<System.Guid, Ref>().ConvertUsing(Ref.From); } }
        """;

    [Fact]
    public void PairMapping_AddedInAnotherFile_IM0002Disappears_PropertyConverted()
    {
        string[] sources = AssertSameAsFreshRun(new[] { MappingWithValueObject, NoPairMapping }, 1, PairMapping, out string[]? ids);

        Assert.Empty(ids);
        Assert.Contains(sources, s => s.Contains("Target = MapToRefCore(source.Target),"));
    }

    [Fact]
    public void PairMapping_RemovedInAnotherFile_IM0002Appears_PropertyNotGenerated()
    {
        string[] sources = AssertSameAsFreshRun(new[] { MappingWithValueObject, PairMapping }, 1, NoPairMapping, out string[]? ids);

        Assert.Equal(new[] { "IM0002" }, ids);
        Assert.DoesNotContain(sources, s => s.Contains("Target ="));
    }

    /// <summary>
    /// Runs the driver on <paramref name="before"/>, then on <paramref name="after"/>, and asserts that the
    /// diagnostics and generated sources of the second run equal those of a fresh driver on <paramref name="after"/>.
    /// Returns the sorted diagnostic IDs of the second run.
    /// </summary>
    private static string[] AssertSameAsFreshRun(string before, string after)
    {
        AssertSameAsFreshRun(new[] { before }, 0, after, out string[]? ids);
        return ids;
    }

    /// <summary>
    /// Like <see cref="AssertSameAsFreshRun(string, string)"/> for several files, of which only the one at
    /// <paramref name="editedIndex"/> changes. Returns the generated sources of the second run.
    /// </summary>
    private static string[] AssertSameAsFreshRun(string[] before, int editedIndex, string after, out string[] ids)
    {
        var first = GeneratorTestHelper.CreateCompilation(before);
        var tree = first.SyntaxTrees.ElementAt(editedIndex);
        var second = first.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(after, (CSharpParseOptions)tree.Options));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = driver.RunGenerators(first);
        var incremental = driver.RunGenerators(second).GetRunResult();

        var fresh = CSharpGeneratorDriver.Create(new IronMapperGenerator()).RunGenerators(second).GetRunResult();

        Assert.Equal(Describe(fresh.Diagnostics), Describe(incremental.Diagnostics));
        Assert.Equal(Sources(fresh), Sources(incremental));

        ids = incremental.Diagnostics.Select(d => d.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();
        return incremental.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    private static IEnumerable<string> Describe(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}").OrderBy(s => s, System.StringComparer.Ordinal);

    private static IEnumerable<string> Sources(GeneratorDriverRunResult result)
        => result.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => $"{s.HintName}\n{s.SourceText}")
            .OrderBy(s => s, System.StringComparer.Ordinal);
}
