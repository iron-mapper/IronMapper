using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronMapper.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Utilities for running <see cref="IronMapperGenerator"/> against in-memory source code
/// and inspecting the results.
/// </summary>
internal static class GeneratorTestHelper
{
    /// <summary>
    /// Compiles <paramref name="source"/>, runs the generator, and returns the generated
    /// source texts together with any diagnostics the generator reported.
    /// </summary>
    public static (IReadOnlyList<string> generatedSources, IReadOnlyList<Diagnostic> diagnostics)
        RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = GetFrameworkReferences()
            .Append(MetadataReference.CreateFromFile(typeof(MapToAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new IronMapperGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var runResult = driver.GetRunResult();

        var generatedSources = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .ToList();

        // Merge generator-level diagnostics with any errors produced by the driver itself.
        var diagnostics = runResult.Diagnostics.ToList();

        return (generatedSources, diagnostics);
    }

    /// <summary>
    /// Like <see cref="RunGenerator"/>, but also compiles the user code together with the generated
    /// sources (with XML documentation comments parsed) and returns all compiler errors plus any
    /// warning reported inside a generated file (e.g. CS0050/CS0051, invalid <c>cref</c>).
    /// </summary>
    public static (IReadOnlyList<string> generatedSources, IReadOnlyList<Diagnostic> generatorDiagnostics, IReadOnlyList<Diagnostic> compilationErrors)
        RunGeneratorAndCompile(string source)
    {
        var compilation = CreateCompilation(source);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out _);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees.Select(t => t.ToString()).ToList();
        var generatedTrees = new HashSet<SyntaxTree>(runResult.GeneratedTrees);
        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        || (d.Severity == DiagnosticSeverity.Warning
                            && d.Location.SourceTree is not null
                            && generatedTrees.Contains(d.Location.SourceTree)))
            .ToList();

        return (generatedSources, runResult.Diagnostics.ToList(), compilationErrors);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = GetFrameworkReferences()
            .Append(MetadataReference.CreateFromFile(typeof(MapToAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static IEnumerable<MetadataReference> GetFrameworkReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedPlatformAssemblies))
            return Array.Empty<MetadataReference>();

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
    }
}
