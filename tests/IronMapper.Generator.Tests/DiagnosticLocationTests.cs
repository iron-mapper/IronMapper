using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Where each diagnostic points in the user's code (docs/diagnostics.md lists the same targets). The location is a
/// source location in the user's syntax tree, so the IDE underlines it, <c>.editorconfig</c> severities apply and the
/// code fixes find their node.
/// </summary>
public class DiagnosticLocationTests
{
    /// <summary>Runs the generator on the files (path → source) and returns its diagnostics.</summary>
    internal static IReadOnlyList<Diagnostic> Run(params (string Path, string Source)[] files)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(files.Select(f => f.Source))
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(files.Select(f => CSharpSyntaxTree.ParseText(f.Source,
                CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose), f.Path)));
        var result = CSharpGeneratorDriver.Create(new IronMapperGenerator()).RunGenerators(compilation).GetRunResult();
        return result.Diagnostics;
    }

    private static IReadOnlyList<Diagnostic> Run(string source) => Run(("/src/Mappings.cs", source));

    /// <summary>Asserts that <paramref name="diagnostic"/> covers exactly <paramref name="snippet"/> in the file.</summary>
    internal static void AssertAt(Diagnostic diagnostic, string path, string source, string snippet, int occurrence = 0)
    {
        int start = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            start = source.IndexOf(snippet, start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Snippet not found: {snippet}");
        }

        Assert.True(diagnostic.Location.IsInSource, $"{diagnostic.Id} has no source location: {diagnostic}");
        Assert.Equal(path, diagnostic.Location.SourceTree!.FilePath);
        Assert.Equal(snippet, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Equal(start, diagnostic.Location.SourceSpan.Start);
    }

    private static void AssertAt(Diagnostic diagnostic, string source, string snippet, int occurrence = 0)
        => AssertAt(diagnostic, "/src/Mappings.cs", source, snippet, occurrence);

    [Fact]
    public void IM0001_Attribute_PointsToDestinationPropertyName()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }
            public class Dest
            {
                public int Id { get; set; }
                public int Extra { get; set; }
            }
            """;

        AssertAt(Assert.Single(Run(source)), source, "Extra");
    }

    [Fact]
    public void IM0001_DestinationInAnotherFile_PointsToPropertyThere()
    {
        string mapping = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }
            """;
        string dest = """
            namespace App;
            public class Dest
            {
                public int Id { get; set; }
                public int Extra { get; set; }
            }
            """;

        var diagnostic = Assert.Single(Run(("/src/Source.cs", mapping), ("/src/Dest.cs", dest)));
        AssertAt(diagnostic, "/src/Dest.cs", dest, "Extra");
    }

    [Fact]
    public void IM0001_DestinationFromMetadata_PointsToAttribute()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(System.Version))]
            public class Source { public int Major { get; set; } }
            """;

        // System.Version has get-only properties: nothing settable, nothing unmapped. Use a settable BCL type instead.
        string settable = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(System.UriBuilder))]
            public class Source { public string Host { get; set; } = ""; }
            """;

        Assert.Empty(Run(source));
        var diagnostics = Run(settable);
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("IM0001", d.Id);
            AssertAt(d, settable, "MapTo(typeof(System.UriBuilder))");
        });
    }

    [Fact]
    public void IM0003_PointsToAttribute()
    {
        string source = """
            namespace App;
            public class Box<T> { }
            [IronMapper.Attributes.MapTo(typeof(Box<>))]
            public class Source { }
            """;

        AssertAt(Assert.Single(Run(source)), source, "IronMapper.Attributes.MapTo(typeof(Box<>))");
    }

    [Fact]
    public void IM0005_PointsToProfileName()
    {
        string source = """
            namespace App;
            public class EmptyProfile : IronMapper.Configuration.MappingProfile
            {
                public EmptyProfile() { }
            }
            """;

        AssertAt(Assert.Single(Run(source)), source, "EmptyProfile");
    }

    [Fact]
    public void IM0006_PointsToMapPropertyAttribute_WithDestinationPropertiesForTheCodeFix()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dest))]
            public class Source { [MapProperty("FulName")] public string Name { get; set; } = ""; }
            public class Dest { public string FullName { get; set; } = ""; public string Name { get; set; } = ""; }
            """;

        var diagnostic = Assert.Single(Run(source), d => d.Id == "IM0006");
        AssertAt(diagnostic, source, "MapProperty(\"FulName\")");
        Assert.Equal("FullName;Name", diagnostic.Properties["DestinationProperties"]);
    }

    [Fact]
    public void IM0007_PointsToTheDefinitionNotGenerated_WithTheOthersAsAdditionalLocations()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }
            [MapFrom(typeof(Source))]
            public class Dest { public int Id { get; set; } }
            """;

        var diagnostic = Assert.Single(Run(source));
        Assert.Equal("IM0007", diagnostic.Id);
        AssertAt(diagnostic, source, "MapFrom(typeof(Source))");
        var other = Assert.Single(diagnostic.AdditionalLocations);
        Assert.Equal("MapTo(typeof(Dest))", source.Substring(other.SourceSpan.Start, other.SourceSpan.Length));
    }

    [Fact]
    public void IM0008_IM0010_PointToCreateMap()
    {
        string source = """
            using IronMapper.Configuration;
            namespace App;
            public class Source { public int Id { get; set; } }
            public abstract class Dest { public int Id { get; set; } }
            public class Outer { private class Hidden { public int Id { get; set; } } public class P : MappingProfile { public P() { CreateMap<Source, Hidden>(); } } }
            public class P : MappingProfile
            {
                public P() { CreateMap<Source, Dest>(); }
            }
            """;

        var diagnostics = Run(source);
        AssertAt(diagnostics.Single(d => d.Id == "IM0008"), source, "CreateMap<Source, Dest>()");
        AssertAt(diagnostics.Single(d => d.Id == "IM0010"), source, "CreateMap<Source, Hidden>()");
    }

    [Fact]
    public void IM0009_PointsToRecordParameterName()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }
            public record Dest(int Id, string Missing);
            """;

        AssertAt(Run(source).Single(d => d.Id == "IM0009"), source, "Missing");
    }

    [Fact]
    public void IM0011_IM0012_PointToTheMemberInTheLambda()
    {
        string source = """
            using IronMapper.Configuration;
            namespace App;
            public class Source { public int Id { get; set; } }
            public class Dest { public string Text { get; set; } = ""; public string Other { get; set; } = ""; }
            public class P : MappingProfile
            {
                private readonly string _prefix = "x";
                public P()
                {
                    CreateMap<Source, Dest>()
                        .ForMember(d => d.Text, o => o.MapFrom(s => Format(s.Id)))
                        .ForMember(d => d.Other, o => o.MapFrom(s => _prefix + s.Id));
                }
                private static string Format(int id) => id.ToString();
            }
            """;

        var diagnostics = Run(source);
        AssertAt(diagnostics.Single(d => d.Id == "IM0011"), source, "Format");
        AssertAt(diagnostics.Single(d => d.Id == "IM0012"), source, "_prefix", occurrence: 1);
    }

    [Fact]
    public void IM0013_PointsToTheMappingDefinition()
    {
        string source = """
            using IronMapper.Attributes;
            namespace App
            {
                [MapTo(typeof(A.Dto))]
                [MapTo(typeof(B.Dto))]
                public class Source { public int Id { get; set; } }
            }
            namespace App.A { public class Dto { public int Id { get; set; } } }
            namespace App.B { public class Dto { public int Id { get; set; } } }
            """;

        var diagnostics = Run(source).Where(d => d.Id == "IM0013").OrderBy(d => d.Location.SourceSpan.Start).ToList();
        Assert.Equal(2, diagnostics.Count);
        AssertAt(diagnostics[0], source, "MapTo(typeof(A.Dto))");
        AssertAt(diagnostics[1], source, "MapTo(typeof(B.Dto))");
    }

    [Fact]
    public void IM0015_PointsToConverterType()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Interfaces;
            namespace App;
            public sealed class NeedsArg : ITypeConverter<string, string> { public NeedsArg(int x) { } public string Convert(string s) => s; }
            [MapTo(typeof(TagDto))] public class Tag { [MapConverter(typeof(NeedsArg))] public string Label { get; set; } = ""; }
            public class TagDto { public string Label { get; set; } = ""; }
            public class Item { public string Code { get; set; } = ""; }
            public class ItemDto { public string Code { get; set; } = ""; }
            public class P : MappingProfile
            {
                public P() { CreateMap<Item, ItemDto>().ForMember(d => d.Code, o => o.UseConverter<NeedsArg>()); }
            }
            """;

        var diagnostics = Run(source).Where(d => d.Id == "IM0015").OrderBy(d => d.Location.SourceSpan.Start).ToList();
        Assert.Equal(2, diagnostics.Count);
        AssertAt(diagnostics[0], source, "NeedsArg", occurrence: 2);
        AssertAt(diagnostics[1], source, "NeedsArg", occurrence: 3);
    }

    [Fact]
    public void IM0007_ReverseMap_PointsToReverseMapCall()
    {
        string source = """
            using IronMapper.Configuration;
            namespace App;
            public class Source { public int Id { get; set; } }
            public class Dest { public int Id { get; set; } }
            public class P : MappingProfile
            {
                public P()
                {
                    CreateMap<Source, Dest>().ReverseMap();
                    CreateMap<Dest, Source>();
                }
            }
            """;

        var diagnostic = Assert.Single(Run(source));
        Assert.Equal("IM0007", diagnostic.Id);
        AssertAt(diagnostic, source, "ReverseMap");
    }
}
