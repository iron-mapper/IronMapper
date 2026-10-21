using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// MSBuild properties the generator reads (<c>CompilerVisibleProperty</c> in <c>buildTransitive/IronMapper.props</c>,
/// passed as <c>build_property.*</c>). Compared by value: the outputs that use them change only when a property does.
/// </summary>
internal sealed class GeneratorSettings : IEquatable<GeneratorSettings>
{
    /// <summary>MSBuild property that turns on IM0001 for profile mappings (1.x: off by default).</summary>
    public const string ReportUnmappedInProfilesProperty = "IronMapperReportUnmappedInProfiles";

    public GeneratorSettings(bool reportUnmappedInProfiles) => ReportUnmappedInProfiles = reportUnmappedInProfiles;

    /// <summary>
    /// Report IM0001 for destination properties of <c>CreateMap</c> mappings that nothing maps
    /// (<c>&lt;IronMapperReportUnmappedInProfiles&gt;true&lt;/IronMapperReportUnmappedInProfiles&gt;</c>).
    /// </summary>
    public bool ReportUnmappedInProfiles { get; }

    /// <summary>Reads the settings from the global analyzer config options of the project.</summary>
    public static GeneratorSettings From(AnalyzerConfigOptions options)
        => new(IsTrue(options, ReportUnmappedInProfilesProperty));

    private static bool IsTrue(AnalyzerConfigOptions options, string property)
        => options.TryGetValue("build_property." + property, out var value)
           && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool Equals(GeneratorSettings? other)
        => other is not null && ReportUnmappedInProfiles == other.ReportUnmappedInProfiles;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratorSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => ReportUnmappedInProfiles ? 1 : 0;
}
