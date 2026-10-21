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

    /// <summary>MSBuild property: <c>false</c> stops generating the in-place overloads of the assembly.</summary>
    public const string GenerateInPlaceProperty = "IronMapperGenerateInPlace";

    /// <summary>MSBuild property: <c>false</c> stops generating <c>MapToXList</c> for the assembly.</summary>
    public const string GenerateListProperty = "IronMapperGenerateList";

    /// <summary>MSBuild property: <c>false</c> stops generating <c>MapToXArray</c> for the assembly.</summary>
    public const string GenerateArrayProperty = "IronMapperGenerateArray";

    /// <summary>MSBuild property: <c>true</c> marks the generated class with <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public const string ExcludeFromCodeCoverageProperty = "IronMapperExcludeFromCodeCoverage";

    public GeneratorSettings(bool reportUnmappedInProfiles, GeneratedMethods? methods = null,
        bool excludeFromCodeCoverage = false)
    {
        ReportUnmappedInProfiles = reportUnmappedInProfiles;
        Methods = methods ?? GeneratedMethods.All;
        ExcludeFromCodeCoverage = excludeFromCodeCoverage;
    }

    /// <summary>
    /// Report IM0001 for destination properties of <c>CreateMap</c> mappings that nothing maps
    /// (<c>&lt;IronMapperReportUnmappedInProfiles&gt;true&lt;/IronMapperReportUnmappedInProfiles&gt;</c>).
    /// </summary>
    public bool ReportUnmappedInProfiles { get; }

    /// <summary>Overloads generated for every mapping of the assembly unless the mapping overrides them.</summary>
    public GeneratedMethods Methods { get; }

    /// <summary>Mark <c>GeneratedMappers</c> with <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public bool ExcludeFromCodeCoverage { get; }

    /// <summary>Reads the settings from the global analyzer config options of the project.</summary>
    public static GeneratorSettings From(AnalyzerConfigOptions options)
        => new(
            IsTrue(options, ReportUnmappedInProfilesProperty),
            new GeneratedMethods(
                !IsFalse(options, GenerateInPlaceProperty),
                !IsFalse(options, GenerateListProperty),
                !IsFalse(options, GenerateArrayProperty)),
            IsTrue(options, ExcludeFromCodeCoverageProperty));

    private static bool IsTrue(AnalyzerConfigOptions options, string property) => Is(options, property, "true");

    // The overloads are generated unless the property is exactly false: an empty or unknown value keeps the default.
    private static bool IsFalse(AnalyzerConfigOptions options, string property) => Is(options, property, "false");

    private static bool Is(AnalyzerConfigOptions options, string property, string expected)
        => options.TryGetValue("build_property." + property, out var value)
           && string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool Equals(GeneratorSettings? other)
        => other is not null
           && ReportUnmappedInProfiles == other.ReportUnmappedInProfiles
           && Methods.Equals(other.Methods)
           && ExcludeFromCodeCoverage == other.ExcludeFromCodeCoverage;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratorSettings);

    /// <inheritdoc/>
    public override int GetHashCode()
        => ((ReportUnmappedInProfiles ? 1 : 0) | (ExcludeFromCodeCoverage ? 2 : 0)) * 31 + Methods.GetHashCode();
}
