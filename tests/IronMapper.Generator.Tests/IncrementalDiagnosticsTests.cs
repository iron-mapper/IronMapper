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
        var before = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "");
        var after = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "[Ignore] ");

        var ids = AssertSameAsFreshRun(before, after);
        Assert.DoesNotContain("IM0001", ids);
    }

    [Fact]
    public void AttributeMapping_IM0001_AppearsWhenIgnoreRemoved()
    {
        var before = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "[Ignore] ");
        var after = AttributeMappingWithUnmatchedProperty.Replace("/*IGNORE*/", "");

        var ids = AssertSameAsFreshRun(before, after);
        Assert.Contains("IM0001", ids);
    }

    [Fact]
    public void ProfileLambda_OneOfTwoIM0011_DisappearsWhenMemberMadeInternal()
    {
        var before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");
        var after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/", "");

        var ids = AssertSameAsFreshRun(before, after);
        Assert.Equal(new[] { "IM0011" }, ids);
    }

    [Fact]
    public void ProfileLambda_SecondIM0011_AppearsWhenMemberMadePrivate()
    {
        var before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/", "");
        var after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");

        var ids = AssertSameAsFreshRun(before, after);
        Assert.Equal(new[] { "IM0011", "IM0011" }, ids);
    }

    [Fact]
    public void ProfileLambda_IM0011_DisappearsWhenAllMembersMadeInternal()
    {
        var before = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/", "").Replace("/*FORMAT*/", "");
        var after = ProfileWithTwoPrivateMembers.Replace("/*PREFIX*/private", "internal").Replace("/*FORMAT*/private", "internal");

        var ids = AssertSameAsFreshRun(before, after);
        Assert.Empty(ids);
    }

    /// <summary>
    /// Runs the driver on <paramref name="before"/>, then on <paramref name="after"/>, and asserts that the
    /// diagnostics and generated sources of the second run equal those of a fresh driver on <paramref name="after"/>.
    /// Returns the sorted diagnostic IDs of the second run.
    /// </summary>
    private static string[] AssertSameAsFreshRun(string before, string after)
    {
        var first = GeneratorTestHelper.CreateCompilation(new[] { before });
        var tree = first.SyntaxTrees.Single();
        var second = first.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(after, (CSharpParseOptions)tree.Options));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = driver.RunGenerators(first);
        var incremental = driver.RunGenerators(second).GetRunResult();

        var fresh = CSharpGeneratorDriver.Create(new IronMapperGenerator()).RunGenerators(second).GetRunResult();

        Assert.Equal(Describe(fresh.Diagnostics), Describe(incremental.Diagnostics));
        Assert.Equal(Sources(fresh), Sources(incremental));

        return incremental.Diagnostics.Select(d => d.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> Describe(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}").OrderBy(s => s, System.StringComparer.Ordinal);

    private static IEnumerable<string> Sources(GeneratorDriverRunResult result)
        => result.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => $"{s.HintName}\n{s.SourceText}")
            .OrderBy(s => s, System.StringComparer.Ordinal);
}
