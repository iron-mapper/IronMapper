using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "IronMapper";

    /// <summary>
    /// The page of a diagnostic on the project site: one page per code, the code in lowercase with a trailing slash
    /// (<c>https://iron-mapper.ru/diagnostics/im0002/</c>), built from the <c>## IMxxxx — Title</c> sections of
    /// <c>docs/diagnostics.md</c>. Every descriptor gets it through <see cref="Create"/>; the address is stable, so a code
    /// keeps its section in the doc.
    /// </summary>
    internal static string HelpLinkUri(string id) => "https://iron-mapper.ru/diagnostics/" + id.ToLowerInvariant() + "/";

    private static DiagnosticDescriptor Create(string id, string title, string messageFormat,
        DiagnosticSeverity defaultSeverity, string description)
        => new(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: Category,
            defaultSeverity: defaultSeverity,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: HelpLinkUri(id));

    /// <summary>
    /// IM0001 — A destination property has no matching source property and will be left at its default value.
    /// </summary>
    public static readonly DiagnosticDescriptor UnmappedDestinationProperty = Create(
        id: "IM0001",
        title: "Unmapped destination property",
        messageFormat: "Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "Add a matching property to the source type, use [MapProperty] to map from a differently-named property, or use [Ignore] on the destination property to suppress this warning. For profile (CreateMap) mappings it is reported only with <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>; there use ForMember(d => d.X, o => o.Ignore()) or MapFrom.");

    /// <summary>
    /// IM0002 — A destination property cannot be assigned from its source: the types are incompatible, a nested object or
    /// the elements of a collection have no mapping for their type pair, or a nullable nested object goes to a
    /// non-nullable destination. The property is not generated; the third argument says why and what to add
    /// (<see cref="Analysis.PropertyTypeCheck"/>).
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatiblePropertyTypes = Create(
        id: "IM0002",
        title: "Property types are incompatible or have no mapping",
        messageFormat: "Property '{0}' of '{1}' is not mapped: {2}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "The generated assignment would not compile (CS0029/CS0266, or CS1061 for a collection element without a mapping). Convert the value (a converter or MapFrom), add the missing mapping (a code fix adds it for a nested object or a collection element), or ignore the property. The property is not generated; the rest of the mapping is.");

    /// <summary>
    /// IM0003 — The type in <c>typeof(...)</c> of [MapTo] or [MapFrom] resolves but cannot be mapped (an open generic type,
    /// an array, a pointer). A type that does not resolve is not reported: the compiler already reports it (CS0246 and
    /// similar) at the <c>typeof</c>, and the mapping is skipped so that generated code does not repeat the error.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedAttributeType = Create(
        id: "IM0003",
        title: "Type in [MapTo]/[MapFrom] cannot be mapped",
        messageFormat: "Mapping for [{0}(typeof({1}))] on '{2}' was not generated: {3}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "[MapTo] and [MapFrom] take a class, struct or record. A generic type must be closed (typeof(Box<int>), not typeof(Box<>)). Other mappings in the project are still generated.");

    /// <summary>
    /// IM0004 — An automatically mapped nested object closes a cycle of generated mappings (A → B → … → A). Reported only
    /// for cycles through at least one nested object mapped automatically (2.1.0): a cycle only through collections
    /// (a tree such as <c>Category.Children</c>) is not reported, and a property configured with <c>MapFrom</c> or
    /// <c>Ignore</c> breaks the cycle. The third argument is the chain (<see cref="Analysis.MappingCycles"/>).
    /// </summary>
    public static readonly DiagnosticDescriptor CyclicMapping = Create(
        id: "IM0004",
        title: "Nested object mapping closes a cycle",
        messageFormat: "Property '{0}' of '{1}' is mapped automatically and closes a cycle of mappings: {2}. The mapping recurses without end only if the objects form a cycle at run time; break the cycle with Ignore or MapFrom on a property of the chain, or suppress IM0004 for models that are always finite (#pragma warning disable IM0004, or dotnet_diagnostic.IM0004.severity = none in .editorconfig).",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "A cycle of types is not an endless recursion by itself: a manager of a manager (Employee.Manager) ends with null. It recurses forever, and fails with a StackOverflowException, only when the data has a cycle, such as an order whose customer lists that order (Order.Customer.Orders). The generated code does not track mapped objects. Cycles only through collections are not reported.");

    /// <summary>
    /// IM0005 — A MappingProfile subclass defines no mappings in its constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyMappingProfile = Create(
        id: "IM0005",
        title: "Empty MappingProfile",
        messageFormat: "MappingProfile '{0}' has no mappings defined",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "Add at least one CreateMap<TSource, TDest>() call to the profile constructor, or remove the class if it is no longer needed.");

    /// <summary>
    /// IM0006 — A [MapProperty] attribute specifies a destination property name that does not exist on the destination type.
    /// </summary>
    public static readonly DiagnosticDescriptor MapPropertyDestinationNotFound = Create(
        id: "IM0006",
        title: "[MapProperty] destination property not found",
        messageFormat: "[MapProperty] destination '{0}' does not exist in type '{1}'",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "The destination property name specified in [MapProperty] does not match any public, settable property on the destination type. Check the spelling or update the destination type.");

    /// <summary>
    /// IM0007 — The same type pair is defined more than once ([MapTo], [MapFrom], CreateMap, ReverseMap).
    /// Only one definition is generated.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateMappingDefinition = Create(
        id: "IM0007",
        title: "Duplicate mapping definition",
        messageFormat: "Mapping from '{0}' to '{1}' is defined more than once: {2}. Only the definition from {3} is generated; remove the others.",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "A type pair is generated once. Precedence: CreateMap in a MappingProfile, then ReverseMap(), then [MapTo], then [MapFrom]; among definitions of the same kind, the profile (or type) whose full name sorts first, then the first call in the constructor. The profile-based mapping takes precedence over attributes.");

    /// <summary>
    /// IM0008 — The destination type is abstract or an interface and cannot be instantiated.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractDestinationType = Create(
        id: "IM0008",
        title: "Abstract or interface destination type",
        messageFormat: "Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Mapping destinations must be concrete, instantiable types. Map to a concrete class, or provide a custom ITypeConverter that creates the appropriate concrete instance.");

    /// <summary>
    /// IM0009 — The destination is created by a constructor (it has no accessible parameterless constructor), and a
    /// parameter of the constructor that fits best gets no value: no source property of the same name (ignoring case),
    /// the source value cannot be converted, or the parameter is ignored and has no default value. The mapping is not
    /// generated. Until 2.0.0 this was a warning for records only, while the generated object initializer did not compile
    /// (CS7036). The third argument says why and what to do (<see cref="Analysis.ConstructorSelection"/>).
    /// </summary>
    public static readonly DiagnosticDescriptor ConstructorParameterNotMapped = Create(
        id: "IM0009",
        title: "Constructor parameter not mapped",
        messageFormat: "Mapping to '{1}' was not generated: parameter '{0}' of {3} gets no value: {2}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "A destination without an accessible parameterless constructor is created by its constructor with the most parameters that can be mapped, or by the factory of ConstructWith. A parameter takes the source property of the same name (ignoring case, or [MapProperty] on it), ForCtorParam, the MapFrom, UseConverter or Ignore of the property of the same name in ForMember, or its default value. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0017 — Several constructors of the destination fit equally well (the same number of parameters mapped from the
    /// source and the same number of parameters); the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousConstructor = Create(
        id: "IM0017",
        title: "Constructor of the destination is ambiguous",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: the constructors {2} fit equally well (each maps {3} parameter(s) from the source). Choose one in a profile with a lambda that calls it, such as ConstructWith({4}), or remove or change one of the constructors.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Of the accessible constructors whose parameters all get a value, the generator takes the one with the most parameters mapped from the source, then the one with the most parameters. A tie is not resolved by the order of declaration, because reordering the code would silently change the mapping. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0018 — The destination has no constructor that the generated code can call (only private or protected ones, such
    /// as a domain type created by a static factory); the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor NoAccessibleConstructor = Create(
        id: "IM0018",
        title: "Destination has no accessible constructor",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: '{1}' has no public or internal constructor the generated code can call. Create it with its static factory method in a profile: CreateMap<{2}, {3}>().ConstructWith({3}.Create), where Create is the factory method.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "The generated code creates the destination with a parameterless constructor and an object initializer, with a constructor with parameters, or with the factory of ConstructWith in a profile (a static method or a lambda). A constructor with ref or out parameters is not used. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0019 — <c>ForCtorParam</c> names a parameter that the constructor or the factory creating the destination does not
    /// have (or the destination is created with an object initializer); the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownConstructorParameter = Create(
        id: "IM0019",
        title: "ForCtorParam names no parameter",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: ForCtorParam({2}) {3}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "ForCtorParam configures a parameter of the constructor the generator chooses, or of the factory of ConstructWith; names are compared ignoring case. A name that is no parameter would be ignored silently, so the mapping is not generated. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0020 — The argument of <c>ConstructWith</c> (or <c>ConstructUsing</c>) cannot be used: not a static method group
    /// or a lambda with typed parameters, a method the generated code cannot call, a return type other than the
    /// destination; the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidFactory = Create(
        id: "IM0020",
        title: "Factory of ConstructWith cannot be used",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: {2}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "ConstructWith takes a static method group or a lambda with typed parameters, read at compile time; the method must be public or internal (the generated class calls it) and return the destination type or a type derived from it, not a nullable one. The message says what is wrong. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0021 — A mapped <c>init</c> property of a destination created by the factory of <c>ConstructWith</c> cannot be set
    /// after the call; the mapping is generated without it.
    /// </summary>
    public static readonly DiagnosticDescriptor InitPropertyAfterFactory = Create(
        id: "IM0021",
        title: "Init property cannot be set after the factory",
        messageFormat: "Property '{1}.{0}' is not mapped: it is init-only, and the destination is created by {2}, so it cannot be set after the call. Pass the value to the factory, or ignore the property with ForMember(d => d.{0}, o => o.Ignore()).",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "An init property can be set only in an object initializer of new. After the factory of ConstructWith the generated code assigns the properties with a public set; the value that the source has for an init property is not mapped.");

    /// <summary>
    /// IM0022 — The argument of <c>ConvertUsing(…)</c> cannot be used: not a lambda or a static method group written in the
    /// call (a delegate in a variable, an instance method, a method the generated code cannot call); the mapping is not
    /// generated. Up to 2.0.0 such an argument was silently ignored and the mapping was generated property by property.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidConverter = Create(
        id: "IM0022",
        title: "Converter of ConvertUsing cannot be used",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: {2}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "ConvertUsing takes a lambda (s => …) or a static method group (ConvertUsing(UserId.From)), read at compile time: the generated method calls it with the source. A delegate in a variable or an instance method cannot be called by the generated class; the method must be public or internal. The message says what is wrong. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0024 — The argument of a profile setting that the generated code calls — <c>MapFrom(…)</c> (in <c>ForMember</c>,
    /// <c>ForCtorParam</c>), <c>When(…)</c>, <c>BeforeMap(…)</c>, <c>AfterMap(…)</c> — cannot be used: not a lambda or a static
    /// method group written in the call (a delegate in a variable, an instance method, a local function, a method the
    /// generated code cannot call); the mapping is not generated. Up to 2.0.0 such an argument, and a static method group
    /// too, was silently ignored: the member was mapped by name (or CS1061 in the generated file), the mapping had no
    /// condition, the hook was not called.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidProfileMethod = Create(
        id: "IM0024",
        title: "Method of a profile setting cannot be used",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: {2} cannot be used: {3}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "MapFrom, When, BeforeMap and AfterMap take a lambda or a static method group (MapFrom(Helper.Compute), When(Rules.IsActive), AfterMap(Audit.Stamp)), read at compile time: the generated method calls it with the source (and the destination for a hook). A delegate in a variable, a local function or an instance method cannot be called by the generated class; the method must be public or internal. The message says what is wrong. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0025 — The in-place overload of a mapping is generated, and it cannot update every mapped property: an
    /// <c>init</c>-only property (also <c>required init</c>) cannot be assigned after construction, so an existing object
    /// keeps its value; an <c>init</c>-only nested object is updated in place through its getter, but only when the
    /// destination already has one. Up to 2.0.0 these were skipped silently. Reported once per mapping, at the mapping;
    /// not for an in-place overload that changes nothing (it is <c>[Obsolete]</c>) or one that is turned off.
    /// </summary>
    public static readonly DiagnosticDescriptor InPlaceSkipsInitProperties = Create(
        id: "IM0025",
        title: "In-place overload does not update init-only properties",
        messageFormat: "In-place mapping from '{0}' to '{1}' does not update every mapped property: {2}. {3}.",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "An init-only destination property cannot be set on an existing object, so source.MapToX(destination) and IMapper.Map(source, destination) leave it as it is; an init-only nested object is updated through its getter when the destination has one. Ignore the properties if they need not be mapped (a new object then does not get them either), or turn the in-place overload off if it is not needed. The overload is still generated: it updates the other properties.");

    /// <summary>
    /// IM0023 — A value that can be <see langword="null"/> (a nullable reference type) goes where the generated code needs
    /// a non-nullable one: a non-nullable destination property or parameter (<c>string?</c> → <c>string</c>), a converter,
    /// a transformer or an element mapping that takes a non-nullable value. Up to 2.0.0 the compiler reported it inside
    /// the generated file (<c>CS8601</c>, <c>CS8604</c>, <c>CS8619</c>); the generated code now marks that value with
    /// <c>!</c> only together with this warning, so the value is mapped as before and the warning is in the user's code.
    /// </summary>
    public static readonly DiagnosticDescriptor NullableNarrowing = Create(
        id: "IM0023",
        title: "Nullable value mapped to a non-nullable member",
        messageFormat: "{0} of '{1}' can receive null: {2}",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "The source value is declared nullable, and the generated code passes it where null is not expected. The value is mapped as it is (null at run time stays null, or throws where the generated call rejects it), as in 2.0.0, where the compiler warned inside the generated file. Map a non-null value (MapFrom(s => s.X!) or s.X ?? a default), declare the destination nullable, or ignore the member. Not reported when nullable annotations are disabled.");

    /// <summary>
    /// IM0010 — A mapped type (or a type containing it, or a type argument) is private/protected,
    /// so the generated class (<c>GeneratedMappers_{AssemblyName}</c>) cannot reference it. The mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor InaccessibleMappingType = Create(
        id: "IM0010",
        title: "Mapping type is not accessible from generated code",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class in namespace IronMapper.Generated cannot access it. Make the type and all types that contain it public or internal.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Generated mapping methods live in the top-level class IronMapper.Generated.GeneratedMappers_{AssemblyName}. Private, protected and private protected nested types are not visible there. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0011 — A profile lambda references a member or type that the generated class cannot access.
    /// </summary>
    public static readonly DiagnosticDescriptor LambdaMemberNotAccessible = Create(
        id: "IM0011",
        title: "Profile lambda uses a member that generated code cannot access",
        messageFormat: "A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class in namespace IronMapper.Generated, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "MapFrom, When, ConvertUsing, BeforeMap/AfterMap and AddTransformer lambdas are emitted into the generated class, not invoked on the profile. Private and protected members of the profile (or of any other type) are not visible there; internal members are.");

    /// <summary>
    /// IM0012 — A profile lambda captures profile instance state, a local variable or a constructor parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor LambdaCapturesState = Create(
        id: "IM0012",
        title: "Profile lambda captures state",
        messageFormat: "A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Instance fields, properties and methods of the profile, 'this', local variables and parameters of the constructor do not exist in the generated code.");

    /// <summary>
    /// IM0013 — Several mappings from one source type have destination types with the same simple name, so
    /// <c>MapTo{Name}</c> would be declared twice (CS0111). The mapping gets a qualified method name.
    /// </summary>
    public static readonly DiagnosticDescriptor QualifiedMethodName = Create(
        id: "IM0013",
        title: "Mapping method name qualified to avoid a collision",
        messageFormat: "Several mappings from '{0}' have a destination type named '{1}', so the mapping to '{2}' is generated as '{3}' instead of '{4}'. Call it as source.{3}() or IronMapper.Generated.{5}.{3}(source); the collection methods are {3}List and {3}Array.",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "The method name is qualified with the namespace and containing types of the destination (fewest trailing segments that make the names unique), then with its type arguments. Mappings without a collision keep MapTo{Name}. Rename one of the destination types to get the short name back, or suppress the warning with <NoWarn>IM0013</NoWarn>.");

    /// <summary>
    /// IM0014 — A method name collision could not be resolved by qualifying the name; the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableMethodNameCollision = Create(
        id: "IM0014",
        title: "Mapping method name collision cannot be resolved",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: another mapping from '{0}' has a destination type named '{2}', and every qualified method name (for example '{3}') is already used by another mapping. Rename one of the destination types.",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Other mappings in the project are still generated.");

    /// <summary>
    /// IM0015 — A converter (<c>[MapConverter]</c>, <c>UseConverter&lt;T&gt;()</c>, <c>ConvertUsing&lt;T&gt;()</c>) cannot be
    /// created by the generated code with <c>new T()</c>; the mapping is not generated.
    /// </summary>
    public static readonly DiagnosticDescriptor ConverterCannotBeCreated = Create(
        id: "IM0015",
        title: "Converter cannot be created by generated code",
        messageFormat: "Mapping from '{0}' to '{1}' was not generated: converter '{2}' ({3}) cannot be created with 'new {2}()' because {4}",
        defaultSeverity: DiagnosticSeverity.Error,
        description: "Generated code creates converters itself; it does not resolve them from dependency injection. Use a non-abstract class or a struct that the generated class can access, with a public or internal parameterless constructor. Other mappings in the project are still generated.");

    /// <summary>
    /// IM0016 — An argument of <c>GenerateMethods(...)</c> in a profile is not a constant, so the generator cannot read
    /// it; the assembly value applies for that overload.
    /// </summary>
    public static readonly DiagnosticDescriptor NonConstantGenerateMethodsArgument = Create(
        id: "IM0016",
        title: "GenerateMethods argument is not a constant",
        messageFormat: "Argument '{0}' of GenerateMethods in MappingProfile '{1}' is not a constant ('{2}'); the generator reads it at compile time, so the value for the assembly applies. Use true, false or null.",
        defaultSeverity: DiagnosticSeverity.Warning,
        description: "GenerateMethods(inPlace, list, array) is read by the source generator, not executed. Pass literal true, false or null (or a constant); null or an omitted argument uses the MSBuild property IronMapperGenerateInPlace/List/Array.");
}
