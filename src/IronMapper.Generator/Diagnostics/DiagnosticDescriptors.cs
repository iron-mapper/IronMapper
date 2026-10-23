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
        description: "Add a matching property to the source type, use [MapProperty] to map from a differently-named property, or use [Ignore] on the destination property to suppress this warning. For profile (CreateMap) mappings it is reported only with <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>; there use ForMember(d => d.X, o => o.Ignore()) or MapFrom.");

    /// <summary>
    /// IM0002 — A destination property cannot be assigned from its source: the types are incompatible, a nested object is
    /// not mapped automatically, or the elements of a collection have no mapping. The property is not generated; the
    /// third argument says why and what to add (<see cref="Analysis.PropertyTypeCheck"/>).
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatiblePropertyTypes = new(
        id: "IM0002",
        title: "Property types are incompatible or have no mapping",
        messageFormat: "Property '{0}' of '{1}' is not mapped: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated assignment would not compile (CS0029/CS0266, or CS1061 for a collection element without a mapping). Convert the value (a converter or MapFrom), add the missing mapping, or ignore the property. The property is not generated; the rest of the mapping is.");

    /// <summary>
    /// IM0003 — The type in <c>typeof(...)</c> of [MapTo] or [MapFrom] resolves but cannot be mapped (an open generic type,
    /// an array, a pointer). A type that does not resolve is not reported: the compiler already reports it (CS0246 and
    /// similar) at the <c>typeof</c>, and the mapping is skipped so that generated code does not repeat the error.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedAttributeType = new(
        id: "IM0003",
        title: "Type in [MapTo]/[MapFrom] cannot be mapped",
        messageFormat: "Mapping for [{0}(typeof({1}))] on '{2}' was not generated: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[MapTo] and [MapFrom] take a class, struct or record. A generic type must be closed (typeof(Box<int>), not typeof(Box<>)). Other mappings in the project are still generated.");

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
    /// IM0007 — The same type pair is defined more than once ([MapTo], [MapFrom], CreateMap, ReverseMap).
    /// Only one definition is generated.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateMappingDefinition = new(
        id: "IM0007",
        title: "Duplicate mapping definition",
        messageFormat: "Mapping from '{0}' to '{1}' is defined more than once: {2}. Only the definition from {3} is generated; remove the others.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A type pair is generated once. Precedence: CreateMap in a MappingProfile, then ReverseMap(), then [MapTo], then [MapFrom]; among definitions of the same kind, the profile (or type) whose full name sorts first, then the first call in the constructor. The profile-based mapping takes precedence over attributes.");

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

    /// <summary>
    /// IM0010 — A mapped type (or a type containing it, or a type argument) is private/protected,
    /// so the generated class (<c>GeneratedMappers_{AssemblyName}</c>) cannot reference it. The mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor InaccessibleMappingType = new(
        id: "IM0010",
        title: "Mapping type is not accessible from generated code",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class in namespace IronMapper.Generated cannot access it. Make the type and all types that contain it public or internal.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated mapping methods live in the top-level class IronMapper.Generated.GeneratedMappers_{AssemblyName}. Private, protected and private protected nested types are not visible there. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0011 — A profile lambda references a member or type that the generated class cannot access.
    /// </summary>
    public static readonly DiagnosticDescriptor LambdaMemberNotAccessible = new(
        id: "IM0011",
        title: "Profile lambda uses a member that generated code cannot access",
        messageFormat: "A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class in namespace IronMapper.Generated, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MapFrom, When, ConvertUsing, BeforeMap/AfterMap and AddTransformer lambdas are emitted into the generated class, not invoked on the profile. Private and protected members of the profile (or of any other type) are not visible there; internal members are.");

    /// <summary>
    /// IM0012 — A profile lambda captures profile instance state, a local variable or a constructor parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor LambdaCapturesState = new(
        id: "IM0012",
        title: "Profile lambda captures state",
        messageFormat: "A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Instance fields, properties and methods of the profile, 'this', local variables and parameters of the constructor do not exist in the generated code.");

    /// <summary>
    /// IM0013 — Several mappings from one source type have destination types with the same simple name, so
    /// <c>MapTo{Name}</c> would be declared twice (CS0111). The mapping gets a qualified method name.
    /// </summary>
    public static readonly DiagnosticDescriptor QualifiedMethodName = new(
        id: "IM0013",
        title: "Mapping method name qualified to avoid a collision",
        messageFormat: "Several mappings from '{0}' have a destination type named '{1}', so the mapping to '{2}' is generated as '{3}' instead of '{4}'. Call it as source.{3}() or IronMapper.Generated.{5}.{3}(source); the collection methods are {3}List and {3}Array.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The method name is qualified with the namespace and containing types of the destination (fewest trailing segments that make the names unique), then with its type arguments. Mappings without a collision keep MapTo{Name}. Rename one of the destination types to get the short name back, or suppress the warning with <NoWarn>IM0013</NoWarn>.");

    /// <summary>
    /// IM0014 — A method name collision could not be resolved by qualifying the name; the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableMethodNameCollision = new(
        id: "IM0014",
        title: "Mapping method name collision cannot be resolved",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: another mapping from '{0}' has a destination type named '{2}', and every qualified method name (for example '{3}') is already used by another mapping. Rename one of the destination types.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Other mappings in the project are still generated.");

    /// <summary>
    /// IM0015 — A converter (<c>[MapConverter]</c>, <c>UseConverter&lt;T&gt;()</c>, <c>ConvertUsing&lt;T&gt;()</c>) cannot be
    /// created by the generated code with <c>new T()</c>; the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor ConverterCannotBeCreated = new(
        id: "IM0015",
        title: "Converter cannot be created by generated code",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: converter '{2}' ({3}) cannot be created with 'new {2}()' because {4}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated code creates converters itself; it does not resolve them from dependency injection. Use a non-abstract class or a struct that the generated class can access, with a public or internal parameterless constructor. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0016 — An argument of <c>GenerateMethods(...)</c> in a profile is not a constant, so the generator cannot read
    /// it; the assembly value applies for that overload.
    /// </summary>
    public static readonly DiagnosticDescriptor NonConstantGenerateMethodsArgument = new(
        id: "IM0016",
        title: "GenerateMethods argument is not a constant",
        messageFormat: "Argument '{0}' of GenerateMethods in MappingProfile '{1}' is not a constant ('{2}'); the generator reads it at compile time, so the value for the assembly applies. Use true, false or null.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "GenerateMethods(inPlace, list, array) is read by the source generator, not executed. Pass literal true, false or null (or a constant); null or an omitted argument uses the MSBuild property IronMapperGenerateInPlace/List/Array.");
}
