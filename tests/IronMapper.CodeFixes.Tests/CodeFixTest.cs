using System;
using System.Collections.Generic;
using System.IO;
using IronMapper.Attributes;
using IronMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace IronMapper.CodeFixes.Tests;

/// <summary>
/// <see cref="CSharpCodeFixTest{TAnalyzer, TCodeFix, TVerifier}"/> with the IronMapper generator attached: the generator
/// reports the diagnostics (there is no analyzer), the code fix fixes them. References are the running .NET and the
/// IronMapper runtime assembly; generated files are not compared (snapshots cover them).
/// </summary>
internal sealed class CodeFixTest<TCodeFix> : CSharpCodeFixTest<EmptyDiagnosticAnalyzer, TCodeFix, DefaultVerifier>
    where TCodeFix : CodeFixProvider, new()
{
    /// <summary>Line endings follow the platform: the code fixes insert <see cref="Environment.NewLine"/> (formatter default).</summary>
    public CodeFixTest(string source, string fixedSource)
    {
        TestCode = source.ReplaceLineEndings();
        FixedCode = fixedSource.ReplaceLineEndings();
        if (source == fixedSource)
        {
            // No fix expected: the fixed state keeps every diagnostic of the markup, fixable or not.
            FixedState.MarkupHandling = MarkupMode.Allow;
        }
        ReferenceAssemblies = new ReferenceAssemblies("net10.0");
        TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;

        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(path));
            FixedState.AdditionalReferences.Add(MetadataReference.CreateFromFile(path));
        }

        TestState.AdditionalReferences.Add(typeof(MapToAttribute).Assembly);
        FixedState.AdditionalReferences.Add(typeof(MapToAttribute).Assembly);
    }

    /// <summary>Adds the same file to the test and the fixed state.</summary>
    public void AddUnchangedSource(string path, string source)
    {
        TestState.Sources.Add((path, source.ReplaceLineEndings()));
        FixedState.Sources.Add((path, source.ReplaceLineEndings()));
    }

    /// <summary>Sets an MSBuild property the generator reads (<c>build_property.*</c> in a global analyzer config).</summary>
    public void SetBuildProperty(string name, string value)
    {
        var config = $"is_global = true{Environment.NewLine}build_property.{name} = {value}{Environment.NewLine}";
        TestState.AnalyzerConfigFiles.Add(("/.globalconfig", config));
        FixedState.AnalyzerConfigFiles.Add(("/.globalconfig", config));
    }

    protected override IEnumerable<Type> GetSourceGenerators() => new[] { typeof(IronMapperGenerator) };
}
