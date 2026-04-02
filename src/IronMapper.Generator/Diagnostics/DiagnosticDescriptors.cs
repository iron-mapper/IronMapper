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

    /// <summary>
    /// IM0005 — A MappingProfile subclass defines no mappings in its constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyMappingProfile = new(
        id: "IM0005",
        title: "Empty MappingProfile",
        messageFormat: "MappingProfile '{0}' has no mappings defined",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Add at least one CreateMap<TSource, TDest>() call to the profile constructor, or remove the class if it is no longer needed.");

    /// <summary>
    /// IM0006 — A [MapProperty] attribute specifies a destination property name that does not exist on the destination type.
    /// </summary>
    public static readonly DiagnosticDescriptor MapPropertyDestinationNotFound = new(
        id: "IM0006",
        title: "[MapProperty] destination property not found",
        messageFormat: "[MapProperty] destination '{0}' does not exist in type '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The destination property name specified in [MapProperty] does not match any public, settable property on the destination type. Check the spelling or update the destination type.");

    /// <summary>
    /// IM0007 — Both a [MapTo]/[MapFrom] attribute and a MappingProfile define a mapping for the same type pair.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateMappingDefinition = new(
        id: "IM0007",
        title: "Duplicate mapping definition",
        messageFormat: "Both an attribute and a MappingProfile define a mapping from '{0}' to '{1}'. The profile-based mapping takes precedence.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Remove either the [MapTo]/[MapFrom] attribute or the corresponding CreateMap<,>() call from the profile to avoid duplicate mapping definitions.");

    /// <summary>
    /// IM0008 — The destination type is abstract or an interface and cannot be instantiated.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractDestinationType = new(
        id: "IM0008",
        title: "Abstract or interface destination type",
        messageFormat: "Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mapping destinations must be concrete, instantiable types. Map to a concrete class, or provide a custom ITypeConverter that creates the appropriate concrete instance.");

    /// <summary>
    /// IM0009 — A record destination type has a primary constructor parameter with no matching source property.
    /// </summary>
    public static readonly DiagnosticDescriptor RecordParameterNotMapped = new(
        id: "IM0009",
        title: "Record constructor parameter not mapped",
        messageFormat: "Constructor parameter '{0}' in record '{1}' has no matching source property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Records are initialized via object initializer using their init-only properties. Ensure the source type has a property matching each primary constructor parameter name, or use [Ignore] on the record property to suppress this warning.");
}
