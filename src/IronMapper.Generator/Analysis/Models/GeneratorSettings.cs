using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// MSBuild properties the generator reads (<c>CompilerVisibleProperty</c> in <c>buildTransitive/IronMapper.props</c>,
/// passed as <c>build_property.*</c>). Compared by value: the outputs that use them change only when a property does.
/// </summary>
internal sealed class GeneratorSettings : IEquatable<GeneratorSettings>
{
    /// <summary>MSBuild property that turns on IM0001 for profile mappings (off by default).</summary>
    public const string ReportUnmappedInProfilesProperty = "IronMapperReportUnmappedInProfiles";

    /// <summary>MSBuild property: <c>false</c> stops generating the in-place overloads of the assembly.</summary>
    public const string GenerateInPlaceProperty = "IronMapperGenerateInPlace";

    /// <summary>MSBuild property: <c>false</c> stops generating <c>MapToXList</c> for the assembly.</summary>
    public const string GenerateListProperty = "IronMapperGenerateList";

    /// <summary>MSBuild property: <c>false</c> stops generating <c>MapToXArray</c> for the assembly.</summary>
    public const string GenerateArrayProperty = "IronMapperGenerateArray";

    /// <summary>MSBuild property: <c>true</c> marks the generated class with <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public const string ExcludeFromCodeCoverageProperty = "IronMapperExcludeFromCodeCoverage";

    /// <summary>MSBuild property: <c>false</c> leaves <c>[GeneratedCode]</c> off the generated class (on by default).</summary>
    public const string GeneratedCodeAttributeProperty = "IronMapperGeneratedCodeAttribute";

    /// <summary>
    /// MSBuild property: <c>false</c> set in the project generates no registration for <c>IMapper</c> (item 2.16 of the
    /// plan). The automatic <c>false</c> of a trimmed or NativeAOT publish goes only into the feature switch
    /// (<c>buildTransitive/IronMapper.targets</c>), so it does not reach the generator.
    /// </summary>
    public const string RegistrationProperty = "IronMapperRegistration";

    public GeneratorSettings(bool reportUnmappedInProfiles, GeneratedMethods? methods = null,
        bool excludeFromCodeCoverage = false, bool generatedCodeAttribute = true, bool registration = true)
    {
        Registration = registration;
        ReportUnmappedInProfiles = reportUnmappedInProfiles;
        Methods = methods ?? GeneratedMethods.All;
        ExcludeFromCodeCoverage = excludeFromCodeCoverage;
        GeneratedCodeAttribute = generatedCodeAttribute;
    }

    /// <summary>
    /// Report IM0001 for destination properties of <c>CreateMap</c> mappings that nothing maps
    /// (<c>&lt;IronMapperReportUnmappedInProfiles&gt;true&lt;/IronMapperReportUnmappedInProfiles&gt;</c>).
    /// </summary>
    public bool ReportUnmappedInProfiles { get; }

    /// <summary>Overloads generated for every mapping of the assembly unless the mapping overrides them.</summary>
    public GeneratedMethods Methods { get; }

    /// <summary>Mark the generated class with <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public bool ExcludeFromCodeCoverage { get; }

    /// <summary>
    /// Mark the generated class with <c>[GeneratedCode]</c> (default). Off with
    /// <c>&lt;IronMapperGeneratedCodeAttribute&gt;false&lt;/IronMapperGeneratedCodeAttribute&gt;</c>: coverage tools that
    /// exclude generated code by that attribute (coverlet <c>ExcludeByAttribute=GeneratedCodeAttribute</c>) then measure it.
    /// </summary>
    public bool GeneratedCodeAttribute { get; }

    /// <summary>
    /// Generate the registration for <c>IMapper</c> (default): <c>GeneratedMappers.Registration.g.cs</c> and the private
    /// in-place methods it calls. Off with <c>&lt;IronMapperRegistration&gt;false&lt;/IronMapperRegistration&gt;</c>.
    /// </summary>
    public bool Registration { get; }

    /// <summary>Reads the settings from the global analyzer config options of the project.</summary>
    public static GeneratorSettings From(AnalyzerConfigOptions options)
        => new(
            IsTrue(options, ReportUnmappedInProfilesProperty),
            new GeneratedMethods(
                !IsFalse(options, GenerateInPlaceProperty),
                !IsFalse(options, GenerateListProperty),
                !IsFalse(options, GenerateArrayProperty)),
            IsTrue(options, ExcludeFromCodeCoverageProperty),
            !IsFalse(options, GeneratedCodeAttributeProperty),
            !IsFalse(options, RegistrationProperty));

    private static bool IsTrue(AnalyzerConfigOptions options, string property) => Is(options, property, "true");

    // The overloads and [GeneratedCode] are generated unless the property is exactly false: an empty or unknown value keeps
    // the default.
    private static bool IsFalse(AnalyzerConfigOptions options, string property) => Is(options, property, "false");

    private static bool Is(AnalyzerConfigOptions options, string property, string expected)
        => options.TryGetValue("build_property." + property, out string? value)
           && string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool Equals(GeneratorSettings? other)
        => other is not null
           && ReportUnmappedInProfiles == other.ReportUnmappedInProfiles
           && Methods.Equals(other.Methods)
           && ExcludeFromCodeCoverage == other.ExcludeFromCodeCoverage
           && GeneratedCodeAttribute == other.GeneratedCodeAttribute
           && Registration == other.Registration;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratorSettings);

    /// <inheritdoc/>
    public override int GetHashCode()
        => ((ReportUnmappedInProfiles ? 1 : 0) | (ExcludeFromCodeCoverage ? 2 : 0) | (GeneratedCodeAttribute ? 4 : 0) | (Registration ? 8 : 0)) * 31 + Methods.GetHashCode();
}
