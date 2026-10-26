using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// IM0001 for profile mappings (issue #22): off by default, on with the MSBuild property
/// <c>IronMapperReportUnmappedInProfiles</c> (passed to the generator as <c>build_property.*</c>). The severity is set the
/// standard way, <c>dotnet_diagnostic.IM0001.severity</c> in <c>.editorconfig</c>, which the compiler applies per syntax
/// tree (checked here with the compiler's own <c>.editorconfig</c> parser).
/// </summary>
public class ProfileUnmappedPropertyTests
{
    private const string Path = "/src/Mappings.cs";

    private const string Profile = """
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Interfaces;

        namespace App;

        public sealed class Upper : ITypeConverter<string, string> { public string Convert(string s) => s.ToUpperInvariant(); }
        public class Address { public string City { get; set; } = ""; }
        public class Order
        {
            public int Id { get; set; }
            public string First { get; set; } = "";
            public string Code { get; set; } = "";
            public Address Address { get; set; } = new();
        }
        public class OrderDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Secret { get; set; } = "";
            public string Code { get; set; } = "";
            public string Label { get; set; } = "";
            public string City { get; set; } = "";
            [Ignore] public string Ignored { get; set; } = "";
            public string Missing { get; set; } = "";
            public string AlsoMissing { get; set; } = "";
        }
        public class Whole { public int Id { get; set; } }
        public class WholeDto { public string Text { get; set; } = ""; public string Missing { get; set; } = ""; }
        public sealed class WholeConverter : ITypeConverter<Whole, WholeDto> { public WholeDto Convert(Whole w) => new(); }
        public class Lambda { public int Id { get; set; } }
        public class LambdaDto { public string Missing { get; set; } = ""; }

        public class OrderProfile : MappingProfile
        {
            public OrderProfile()
            {
                CreateMap<Order, OrderDto>()
                    .ForMember(d => d.FullName, o => o.MapFrom(s => s.First))
                    .ForMember(d => d.Secret, o => o.Ignore())
                    .ForMember(d => d.Label, o => o.UseConverter<Upper>())
                    .IncludeMembers(s => s.Address)
                    .ReverseMap();
                CreateMap<Whole, WholeDto>().ConvertUsing<WholeConverter>();
                CreateMap<Lambda, LambdaDto>().ConvertUsing(s => new LambdaDto());
            }
        }
        """;

    [Fact]
    public void Default_NoIM0001ForProfiles()
        => Assert.Empty(Run(Profile, reportUnmapped: null));

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    public void PropertyNotTrue_NoIM0001(string value)
        => Assert.Empty(Run(Profile, reportUnmapped: value));

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData(" TRUE ")]
    public void PropertyTrue_IM0001ForUnmatchedProperties_AtCreateMap(string value)
    {
        var diagnostics = Run(Profile, reportUnmapped: value);

        // FullName (MapFrom), Secret (Ignore), Label (UseConverter), City (IncludeMembers), Id and Code (by name) and
        // Ignored ([Ignore]) are mapped. ConvertUsing replaces the whole mapping; ReverseMap is not checked.
        Assert.Equal(new[] { "AlsoMissing", "Missing" }, diagnostics.Select(d => d.Properties["PropertyName"]).OrderBy(n => n));
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("IM0001", d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("on destination type 'OrderDto' has no corresponding source property in 'Order'", d.GetMessage());
            DiagnosticLocationTests.AssertAt(d, Path, Profile, "CreateMap<Order, OrderDto>()");
        });
    }

    [Fact]
    public void PropertyTrue_GeneratedCodeUnchanged()
    {
        string[] off = RunSources(Profile, reportUnmapped: null);
        string[] on = RunSources(Profile, reportUnmapped: "true");
        Assert.Equal(off, on);
    }

    [Fact]
    public void EditorConfigSeverityError_MakesIM0001AnError_ForProfilesAndAttributes()
    {
        string source = Profile + """

            [MapTo(typeof(TagDto))] public class Tag { public int Id { get; set; } }
            public class TagDto { public int Id { get; set; } public int Extra { get; set; } }
            """;

        var diagnostics = Run(source, reportUnmapped: "true", editorConfig: """
            root = true

            [*.cs]
            dotnet_diagnostic.IM0001.severity = error
            """);

        Assert.Equal(3, diagnostics.Count); // Missing, AlsoMissing (profile), Extra (attribute)
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("IM0001", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        });
    }

    [Fact]
    public void EditorConfigSeverityNone_SuppressesIM0001()
        => Assert.Empty(Run(Profile, reportUnmapped: "true", editorConfig: """
            root = true

            [*.cs]
            dotnet_diagnostic.IM0001.severity = none
            """));

    private static IReadOnlyList<Diagnostic> Run(string source, string? reportUnmapped, string? editorConfig = null)
        => CreateDriver(source, reportUnmapped, editorConfig, out var compilation)
            .RunGenerators(compilation).GetRunResult().Diagnostics;

    private static string[] RunSources(string source, string? reportUnmapped)
        => CreateDriver(source, reportUnmapped, null, out var compilation)
            .RunGenerators(compilation).GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();

    private static GeneratorDriver CreateDriver(string source, string? reportUnmapped, string? editorConfig,
        out Compilation compilation)
    {
        var parseOptions = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, Path);
        var csharp = GeneratorTestHelper.CreateCompilation(System.Array.Empty<string>()).AddSyntaxTrees(tree);

        // .editorconfig as csc reads it: parsed by the compiler, applied to each tree by its path.
        if (editorConfig is not null)
        {
            var set = AnalyzerConfigSet.Create(new[] { AnalyzerConfig.Parse(editorConfig, "/src/.editorconfig") });
            var treeOptions = set.GetOptionsForSourcePath(Path).TreeOptions;
            csharp = csharp.WithOptions(csharp.Options.WithSyntaxTreeOptionsProvider(new TreeOptions(tree, treeOptions)));
        }

        compilation = csharp;
        var globalOptions = new Dictionary<string, string>();
        if (reportUnmapped is not null)
        {
            globalOptions["build_property.IronMapperReportUnmappedInProfiles"] = reportUnmapped;
        }

        return CSharpGeneratorDriver.Create(
            new[] { new IronMapperGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            optionsProvider: new TestOptionsProvider(globalOptions));
    }

    /// <summary>Per-tree diagnostic severities from <c>.editorconfig</c>, as the compiler's own provider passes them.</summary>
    private sealed class TreeOptions : SyntaxTreeOptionsProvider
    {
        private readonly SyntaxTree _tree;
        private readonly ImmutableDictionary<string, ReportDiagnostic> _severities;

        public TreeOptions(SyntaxTree tree, ImmutableDictionary<string, ReportDiagnostic> severities)
        {
            _tree = tree;
            _severities = severities;
        }

        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = default;
            return tree == _tree && _severities.TryGetValue(diagnosticId, out severity);
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = default;
            return false;
        }
    }
}
