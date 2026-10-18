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

    /// <summary>
    /// Like <see cref="RunGeneratorAndCompile"/>, and when there are no errors also emits the assembly, loads it into
    /// an isolated context and returns the result of the static method <c>Program.Run()</c> (a string) declared in
    /// <paramref name="source"/>. <c>runResult</c> is <see langword="null"/> when the code does not compile.
    /// </summary>
    public static (IReadOnlyList<string> generatedSources, IReadOnlyList<Diagnostic> generatorDiagnostics, IReadOnlyList<Diagnostic> compilationErrors, string? runResult)
        RunGeneratorCompileAndRun(string source)
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
        if (compilationErrors.Count > 0)
            return (generatedSources, runResult.Diagnostics.ToList(), compilationErrors, null);

        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException("Emit failed:\n" + string.Join("\n", emit.Diagnostics));

        var context = new System.Runtime.Loader.AssemblyLoadContext($"run-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var program = assembly.GetTypes().Single(t => t.Name == "Program");
            var result = (string)program.GetMethod("Run")!.Invoke(null, null)!;
            return (generatedSources, runResult.Diagnostics.ToList(), compilationErrors, result);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Compiles <paramref name="source"/> as assembly <paramref name="assemblyName"/> with the generator, like a
    /// project with <c>TreatWarningsAsErrors</c>: returns the output compilation and every warning and error
    /// (generator and compiler, user code included). Use <see cref="EmitReference"/> to reference the result
    /// from another compilation.
    /// </summary>
    public static (CSharpCompilation compilation, IReadOnlyList<Diagnostic> warningsAndErrors)
        CompileProject(string assemblyName, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references: GetFrameworkReferences()
                .Append(MetadataReference.CreateFromFile(typeof(MapToAttribute).Assembly.Location))
                .Concat(references),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable)
                // Missing XML comments on user types are not what these tests check (CS1591 is NoWarn in the repo).
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["CS1591"] = ReportDiagnostic.Suppress }));

        CSharpGeneratorDriver.Create(new IronMapperGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var warningsAndErrors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToList();

        return ((CSharpCompilation)outputCompilation, warningsAndErrors);
    }

    /// <summary>Emits <paramref name="compilation"/> and returns the image, as a project reference would see it.</summary>
    public static (MetadataReference reference, byte[] image) EmitReference(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException("Emit failed:\n" + string.Join("\n", result.Diagnostics));

        var image = stream.ToArray();
        return (MetadataReference.CreateFromImage(image), image);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);

    private static CSharpCompilation CreateCompilation(string source) => CreateCompilation(new[] { source });

    /// <summary>Creates the test compilation (one syntax tree per source, generator not run).</summary>
    public static CSharpCompilation CreateCompilation(IEnumerable<string> sources)
    {
        var references = GetFrameworkReferences()
            .Append(MetadataReference.CreateFromFile(typeof(MapToAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions)),
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
