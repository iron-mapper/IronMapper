using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "IronMapper";

    /// <summary>
    /// IM0001 — A destination property has no matching source property and will be left at its default value.
    /// </summary>
    public static readonly DiagnosticDescriptor UnmappedDestinationProperty = new(
        id: "IM0001",
        title: "Unmapped destination property",
        messageFormat: "Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Add a matching property to the source type, use [MapProperty] to map from a differently-named property, or use [Ignore] on the destination property to suppress this warning.");

    /// <summary>
    /// IM0002 — Source and destination property types are incompatible and no converter is specified.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatiblePropertyTypes = new(
        id: "IM0002",
        title: "Incompatible property types",
        messageFormat: "Property '{0}': source type '{1}' cannot be assigned to destination type '{2}'. Add [MapConverter] or implement ITypeConverter<{1}, {2}>.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The source and destination property types are not compatible. Provide a custom ITypeConverter to convert between them.");

    /// <summary>
    /// IM0003 — The type referenced by [MapTo] or [MapFrom] could not be resolved.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableTargetType = new(
        id: "IM0003",
        title: "Unresolvable mapping target type",
        messageFormat: "The type '{0}' referenced in [{1}] could not be resolved in the current compilation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure the target type is accessible from the current project and that all required assembly references are present.");

    /// <summary>
    /// IM0004 — A cyclic mapping was detected (A→B→…→A).
    /// </summary>
    public static readonly DiagnosticDescriptor CyclicMapping = new(
        id: "IM0004",
        title: "Cyclic mapping detected",
        messageFormat: "Cyclic mapping detected: '{0}' maps to '{1}' which directly or indirectly maps back to '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Remove or break the cycle between the two types. Use a custom ITypeConverter to handle self-referential or recursive object graphs.");
}
