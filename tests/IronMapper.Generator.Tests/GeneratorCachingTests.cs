using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;

namespace IronMapper.Generator.Tests;

/// <summary>
/// The incremental pipeline must not redo work when nothing relevant changed: a second run on the same input, and a
/// run after editing a file without mappings, leave every step of the generator <c>Cached</c> or <c>Unchanged</c>.
/// A <c>Modified</c> step means pipeline data that is not compared by value (ISymbol, SyntaxNode, Location,
/// ImmutableArray, a model without value equality), which makes the generator re-run on every keystroke in the IDE.
/// The transforms (<c>*Transform</c>) use the semantic model, so Roslyn runs them again for every new compilation;
/// they return <c>EquatableArray&lt;MappingDescriptor&gt;</c>, so an equal result is reported <c>Unchanged</c> and the
/// steps after them are <c>Cached</c>. Roslyn's own steps (<c>Compilation</c>,
/// <c>compilationAndGroupedNodes_ForAttributeWithMetadataName</c>, …) hold the new compilation and are always
/// <c>Modified</c>; only the generator's named steps and the outputs are checked. That an edit which changes only a
/// diagnostic is not cached is checked in <see cref="IncrementalDiagnosticsTests"/>.
/// The diagnostics output is the exception: it resolves the diagnostics' locations in the current compilation's syntax
/// trees (so .editorconfig severities and code fixes apply), so it runs for every compilation; it generates no code.
/// Source outputs must not depend on locations: moving a mapping (lines added above it) leaves them cached.
/// </summary>
public class GeneratorCachingTests
{
    private readonly ITestOutputHelper _output;

    public GeneratorCachingTests(ITestOutputHelper output) => _output = output;

    private const string Mappings = """
        using System.Collections.Generic;
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Interfaces;

        namespace App;

        [MapTo(typeof(UserDto))]
        public class User
        {
            public int Id { get; set; }
            [MapProperty("FullName")] public string Name { get; set; } = "";
            [MapConverter(typeof(Upper))] public string Code { get; set; } = "";
            public List<Line> Lines { get; set; } = new();
        }
        public class UserDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Code { get; set; } = "";
            public List<LineDto> Lines { get; set; } = new();
        }

        [MapTo(typeof(LineDto))]
        public class Line { public string Text { get; set; } = ""; }
        public class LineDto { public string Text { get; set; } = ""; }

        public sealed class Upper : ITypeConverter<string, string>
        {
            public string Convert(string source) => source.ToUpperInvariant();
        }

        public class Order { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
        public class OrderDto { public string Address { get; set; } = ""; }
        public class OrderProfile : MappingProfile
        {
            public OrderProfile()
            {
                CreateMap<Order, OrderDto>()
                    .ForMember(d => d.Address, o => o.MapFrom(s => s.Street + ", " + s.City))
                    .AfterMap((s, d) => d.Address = d.Address.Trim());
            }
        }
        """;

    private const string Unrelated = """
        namespace App;

        public static class Unrelated
        {
            public static int Value() => 1;
        }
        """;

    private static readonly IncrementalStepRunReason[] CachedReasons =
        { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged };

    /// <summary>Steps of this generator (<see cref="TrackingNames"/>); Roslyn's own steps are not checked.</summary>
    private static readonly string[] GeneratorSteps =
    {
        TrackingNames.MapToTransform, TrackingNames.MapTo,
        TrackingNames.MapFromTransform, TrackingNames.MapFrom,
        TrackingNames.ProfileTransform, TrackingNames.Profiles,
        TrackingNames.Plan, TrackingNames.ClassAccessibility, TrackingNames.CodeOptions, TrackingNames.Registrations,
        TrackingNames.SourcePlan, TrackingNames.MapToSource, TrackingNames.MapFromSource, TrackingNames.ProfilesSource,
        TrackingNames.Settings, TrackingNames.Diagnostics,
        TrackingNames.MethodSettings, TrackingNames.CoverageSettings,
    };

    /// <summary>Steps that only carry data used to generate code; they stay cached when a mapping only moves.</summary>
    private static readonly string[] SourceSteps =
    {
        TrackingNames.SourcePlan, TrackingNames.MapToSource, TrackingNames.MapFromSource, TrackingNames.ProfilesSource,
        TrackingNames.ClassAccessibility, TrackingNames.CodeOptions, TrackingNames.Registrations,
        TrackingNames.MethodSettings, TrackingNames.CoverageSettings,
    };

    private const string MovedMappings = """
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App;
        //LINES
        [MapTo(typeof(UserDto))]
        [MapTo(typeof(Other.UserDto))]
        public class User { public int Id { get; set; } }
        public class UserDto { public int Id { get; set; } public int Extra { get; set; } }

        public class Order { public int Id { get; set; } }
        public class OrderDto { public int Id { get; set; } }
        public class OrderProfile : MappingProfile
        {
            public OrderProfile() { CreateMap<Order, OrderDto>().ReverseMap(); }
        }
        public class EmptyProfile : MappingProfile { public EmptyProfile() { } }
        """;

    private const string OtherUserDto = """
        namespace App.Other;
        public class UserDto { public int Id { get; set; } }
        """;

    [Fact]
    public void SecondRunWithoutChanges_AllStepsCached()
        => AssertCached(RunTwice(edit: false), GeneratorSteps);

    [Fact]
    public void EditOfUnrelatedFile_AllStepsCached()
        => AssertCached(RunTwice(edit: true), GeneratorSteps);

    [Fact]
    public void LinesAddedAboveMappings_SourceOutputsCached_DiagnosticLocationsUpdated()
    {
        // Diagnostics of each origin: IM0001 (attribute), IM0005 (profile), IM0013 (plan).
        var options = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);
        var compilation = GeneratorTestHelper.CreateCompilation(System.Array.Empty<string>()).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(MovedMappings.Replace("//LINES", ""), options, "/src/Mappings.cs"),
            CSharpSyntaxTree.ParseText(OtherUserDto, options, "/src/Other.cs"));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new IronMapperGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var before = driver.GetRunResult().Diagnostics;

        var tree = compilation.SyntaxTrees.First();
        var moved = compilation.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(MovedMappings.Replace("//LINES", "\n\n"), options, "/src/Mappings.cs"));
        var run = driver.RunGenerators(moved).GetRunResult();

        AssertSourceOutputsCached(Assert.Single(run.Results));

        // Every diagnostic is in the new tree, two lines further down.
        var after = run.Diagnostics;
        Assert.Equal(new[] { "IM0001", "IM0005", "IM0013", "IM0013" }, after.Select(d => d.Id).OrderBy(id => id));
        Assert.Equal(before.Length, after.Length);
        foreach (var diagnostic in after)
        {
            Assert.True(diagnostic.Location.IsInSource, diagnostic.ToString());
            Assert.Same(moved.SyntaxTrees.First(), diagnostic.Location.SourceTree);
            var previous = before.Single(d => d.GetMessage() == diagnostic.GetMessage());
            Assert.Equal(previous.Location.GetLineSpan().StartLinePosition.Line + 2,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        }
    }

    private void AssertSourceOutputsCached(GeneratorRunResult result)
    {
        var notCached = new List<string>();
        foreach (var name in SourceSteps)
        {
            if (!result.TrackedSteps.TryGetValue(name, out var runs)) continue;
            foreach (var (_, reason) in runs.SelectMany(r => r.Outputs))
            {
                _output.WriteLine($"{name}: {reason}");
                if (!CachedReasons.Contains(reason)) notCached.Add($"{name}: {reason}");
            }
        }

        var sourceOutputs = result.TrackedOutputSteps.SelectMany(s => s.Value).Where(step => !IsDiagnosticsOutput(step)).ToList();
        Assert.NotEmpty(sourceOutputs);
        foreach (var (_, reason) in sourceOutputs.SelectMany(r => r.Outputs))
            if (!CachedReasons.Contains(reason)) notCached.Add($"SourceOutput: {reason}");

        Assert.True(notCached.Count == 0, "Steps not cached:\n" + string.Join("\n", notCached.Distinct()));
    }

    /// <summary>The diagnostics output: the only output fed by the <see cref="TrackingNames.Diagnostics"/> step.</summary>
    private static bool IsDiagnosticsOutput(IncrementalGeneratorRunStep step)
    {
        var pending = new Stack<IncrementalGeneratorRunStep>(new[] { step });
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Name == TrackingNames.Diagnostics) return true;
            if (current.Name is not null && current != step) continue; // another named step: a different branch
            foreach (var (input, _) in current.Inputs) pending.Push(input);
        }

        return false;
    }

    private static GeneratorRunResult RunTwice(bool edit)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { Mappings, Unrelated });
        var driver = CSharpGeneratorDriver.Create(
                new[] { new IronMapperGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true))
            .RunGenerators(compilation);

        Compilation second = compilation.Clone();
        if (edit)
        {
            var unrelatedTree = compilation.SyntaxTrees.Last();
            second = compilation.ReplaceSyntaxTree(
                unrelatedTree,
                CSharpSyntaxTree.ParseText(Unrelated.Replace("=> 1;", "=> 2;"), (CSharpParseOptions)unrelatedTree.Options));
        }

        return Assert.Single(driver.RunGenerators(second).GetRunResult().Results);
    }

    private void AssertCached(GeneratorRunResult result, IEnumerable<string> stepNames)
    {
        var notCached = new List<string>();
        foreach (var name in stepNames)
        {
            if (!result.TrackedSteps.TryGetValue(name, out var runs)) continue; // e.g. no [MapFrom] in the input
            foreach (var (_, reason) in runs.SelectMany(r => r.Outputs))
            {
                _output.WriteLine($"{name}: {reason}");
                if (!CachedReasons.Contains(reason)) notCached.Add($"{name}: {reason}");
            }
        }

        // Every source output (mapping files, GeneratedMappers.g.cs, the registration) is cached. The diagnostics output
        // runs for every compilation (see the class remarks).
        var sourceOutputs = result.TrackedOutputSteps.SelectMany(s => s.Value).Where(step => !IsDiagnosticsOutput(step)).ToList();
        Assert.NotEmpty(sourceOutputs);
        foreach (var (_, reason) in sourceOutputs.SelectMany(r => r.Outputs))
            if (!CachedReasons.Contains(reason)) notCached.Add($"SourceOutput: {reason}");

        Assert.True(result.TrackedSteps.ContainsKey(TrackingNames.Plan), "Plan step is not tracked.");
        Assert.True(notCached.Count == 0, "Steps not cached:\n" + string.Join("\n", notCached.Distinct()));
    }
}
