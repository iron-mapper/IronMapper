using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Global analyzer config options for a generator driver: the <c>build_property.*</c> values MSBuild passes for the
/// properties listed as <c>CompilerVisibleProperty</c> (buildTransitive/IronMapper.props).
/// </summary>
internal sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly Options _global;

    public TestOptionsProvider(IReadOnlyDictionary<string, string> global) => _global = new Options(global);

    /// <summary>Options with MSBuild properties (<c>name</c> → <c>build_property.name</c>).</summary>
    public static TestOptionsProvider FromBuildProperties(IReadOnlyDictionary<string, string> properties)
    {
        var global = new Dictionary<string, string>();
        foreach (var (name, value) in properties)
        {
            global["build_property." + name] = value;
        }

        return new TestOptionsProvider(global);
    }

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

    private sealed class Options : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(new Dictionary<string, string>());

        private readonly IReadOnlyDictionary<string, string> _values;

        public Options(IReadOnlyDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
