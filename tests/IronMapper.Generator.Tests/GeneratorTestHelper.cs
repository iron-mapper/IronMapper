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

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

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
