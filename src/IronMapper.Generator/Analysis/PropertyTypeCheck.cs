using System.Collections.Immutable;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// IM0002: a destination property that the generated assignment cannot fill. Without the check the generated file failed
/// to compile (<c>CS0029</c>/<c>CS0266</c> for incompatible types and nested objects, <c>CS1061</c> for a collection
/// element without a mapping). A property with IM0002 is not generated; the rest of the mapping is. A nested object
/// whose type pair has a mapping is mapped by it (<see cref="CheckNestedObject"/>).
/// </summary>
internal static class PropertyTypeCheck
{
    /// <summary>
    /// Diagnostic property of an IM0002 for a collection element or a nested object without a mapping: the type pairs
    /// (the source type and its base types and interfaces → the destination type, <c>|</c>-separated). The diagnostic
    /// is reported, and the property skipped, only when the plan generates none of them
    /// (<see cref="MappingPlan.GeneratesAny"/>).
    /// </summary>
    internal const string MissingElementPairsKey = "IfNoMappingFor";

    /// <summary>
    /// Diagnostic properties of an IM0002 "no mapping for the pair", read by the code fix that adds the mapping: the
    /// documentation comment IDs of the source and destination types (<c>T:App.Address</c>; empty for a constructed
    /// generic type, which the fix does not handle), their fully qualified names for the inserted code, and how the
    /// mapping with the diagnostic is declared (<see cref="Origin"/>).
    /// </summary>
    internal const string PairSourceIdKey = "PairSourceId";

    /// <inheritdoc cref="PairSourceIdKey"/>
    internal const string PairDestinationIdKey = "PairDestinationId";

    /// <inheritdoc cref="PairSourceIdKey"/>
    internal const string PairSourceTypeKey = "PairSourceType";

    /// <inheritdoc cref="PairSourceIdKey"/>
    internal const string PairDestinationTypeKey = "PairDestinationType";

    /// <inheritdoc cref="PairSourceIdKey"/>
    internal const string MappingOriginKey = "MappingOrigin";

    /// <summary>
    /// Diagnostic property of the IM0004 candidate of an automatically mapped nested object: the destination property.
    /// <c>CollectDiagnostics</c> reports it only when the property closes a cycle of generated mappings.
    /// </summary>
    internal const string CyclePropertyKey = "CycleProperty";

    /// <summary>How a mapping is declared; decides the hint of the message.</summary>
    internal enum Origin
    {
        Attribute,
        Profile,
        ReverseMap,
    }

    /// <summary>
    /// The IM0002 of a property assigned as <c>destination.X = source.Y</c> (name match, <c>[MapProperty]</c>,
    /// <c>IncludeMembers</c>), or <see langword="null"/> when the value can be assigned. Collections whose element types
    /// differ are mapped element by element and are checked by <see cref="CheckCollection"/> instead.
    /// </summary>
    public static DiagnosticInfo? CheckAssignment(Compilation compilation, ITypeSymbol sourceType, ITypeSymbol destType,
        string sourceDisplay, DestinationMember destProp, INamedTypeSymbol destSymbol, Origin origin, LocationInfo? location)
    {
        if (IsAssignable(compilation, sourceType, destType))
        {
            return null;
        }

        string st = Display(sourceType);
        string dt = Display(destType);
        string reason;
        if (IsObject(sourceType, allowInterface: true) && IsObject(destType))
        {
            // Only IncludeMembers gets here: name-matched nested objects are mapped by CheckNestedObject.
            reason = $"'{sourceDisplay}' is '{st}' and the destination is '{dt}': IncludeMembers does not map nested objects. "
                     + $"Configure it with ForMember(d => d.{destProp.DisplayName}, o => o.MapFrom(...)), with o.UseConverter<T>() "
                     + $"(an ITypeConverter<{st}, {dt}>), or with o.Ignore().";
        }
        else
        {
            reason = $"'{sourceDisplay}' is '{st}', which cannot be assigned to '{dt}'. "
                     + origin switch
                     {
                         Origin.Attribute => $"Add [MapConverter(typeof(...))] (an ITypeConverter<{st}, {dt}>) to the source property, "
                                             + "map it in a profile with ForMember(..., o => o.MapFrom(...)), or mark the destination property [Ignore].",
                         Origin.Profile => $"Configure it with ForMember(d => d.{destProp.DisplayName}, o => o.MapFrom(...)) that converts the value, "
                                           + $"with o.UseConverter<T>() (an ITypeConverter<{st}, {dt}>), or with o.Ignore().",
                         _ => ReverseHint(destProp),
                     };
        }

        return Create(destProp, destSymbol, reason, location);
    }

    /// <summary>
    /// The IM0002 of a <c>MapFrom</c> expression whose type cannot be assigned to the destination property, or
    /// <see langword="null"/>. An expression without a type (<c>null</c>, <c>default</c>) is left to the compiler.
    /// </summary>
    public static DiagnosticInfo? CheckMapFrom(Compilation compilation, ITypeSymbol? expressionType, DestinationMember destProp,
        INamedTypeSymbol destSymbol, LocationInfo? location)
    {
        if (expressionType is null || expressionType.TypeKind == TypeKind.Error
            || IsAssignable(compilation, expressionType, destProp.Type))
        {
            return null;
        }

        string et = Display(expressionType);
        string dt = Display(destProp.Type);
        return Create(destProp, destSymbol,
            $"the MapFrom expression is '{et}', which cannot be assigned to '{dt}'. Convert the value in the expression, "
            + $"or use o.UseConverter<T>() (an ITypeConverter<...>) instead.", location);
    }

    /// <summary>
    /// The IM0002 of a collection property whose elements are mapped one by one when no mapping for the element types
    /// may exist, or <see langword="null"/> when an existing extension method maps them (a referenced assembly's generated
    /// mapping, or a hand-written method that the generated call binds to, see <see cref="IsMappedByExistingMethod"/>). Mappings of this compilation are not known here: the diagnostic carries the
    /// element type pairs (<see cref="MissingElementPairsKey"/>), and the plan decides.
    /// </summary>
    public static DiagnosticInfo? CheckCollection(Compilation compilation, CollectionMapping collection, ITypeSymbol sourceElement,
        ITypeSymbol destElement, string sourceDisplay, DestinationMember destProp, INamedTypeSymbol destSymbol, Origin origin,
        LocationInfo? location, out bool mappedElsewhere)
    {
        mappedElsewhere = IsMappedByExistingMethod(compilation, collection.ElementMethod, sourceElement, destElement);
        if (mappedElsewhere)
        {
            return null;
        }

        string se = Display(sourceElement);
        string de = Display(destElement);
        return MissingPair(destProp, destSymbol,
            $"the elements of '{sourceDisplay}' are '{se}' and the elements of the destination are '{de}', and there is no "
            + $"mapping from '{se}' to '{de}'. {AddMappingHint(se, de)}", location, sourceElement, destElement, origin);
    }

    /// <summary>
    /// A nested object: <paramref name="sourceType"/> and <paramref name="destType"/> are objects that could have a mapping
    /// of their own (see <see cref="IsObject"/>) and the value cannot be assigned as is. Returns <see langword="null"/>
    /// for any other property (checked by <see cref="CheckAssignment"/>). Otherwise the property is mapped by the mapping
    /// of the type pair, adding to <paramref name="diagnostics"/>:
    /// <list type="bullet">
    ///   <item>IM0002 when the source is annotated nullable and the destination is not (no <c>null!</c>); the result is
    ///   then <see cref="NestedObjectMapping.Skipped"/>;</item>
    ///   <item>IM0002 "no mapping for the pair", reported only when the plan generates none of
    ///   <see cref="NestedObjectMapping.Pairs"/> (as for a collection element);</item>
    ///   <item>the IM0004 candidate (<see cref="CyclePropertyKey"/>), reported only on a cycle.</item>
    /// </list>
    /// A mapping in a referenced assembly (or a hand-written method the generated call binds to, see
    /// <see cref="IsMappedByExistingMethod"/>) maps it without either: the pairs are empty.
    /// <see cref="NestedObjectMapping.UpdatesInPlace"/>: the in-place overload may update an existing nested object (a class
    /// whose getter the generated code can call, mapped by a mapping of this compilation; the plan decides whether that
    /// mapping can update an object, <see cref="MappingPlanEntry.CanUpdateInPlace"/>). With a referenced assembly's mapping
    /// (or a hand-written method) only when it has the public in-place overload <c>{method}(this source, dest)</c>
    /// (<see cref="IsUpdatedByExistingMethod"/>); without it (turned off there) a new object is assigned.
    /// </summary>
    public static NestedObjectMapping? CheckNestedObject(Compilation compilation, ITypeSymbol sourceType, ITypeSymbol destType,
        string sourceDisplay, DestinationMember destProp, INamedTypeSymbol destSymbol, Origin origin, LocationInfo? location,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (IsAssignable(compilation, sourceType, destType) || !IsObject(sourceType, allowInterface: true) || !IsObject(destType))
        {
            return null;
        }

        string st = Display(sourceType);
        string dt = Display(destType);
        string method = $"MapTo{destType.Name}";
        string? nullHandling = NullHandling.ForNestedObject(sourceType, destType);
        if (nullHandling is null)
        {
            diagnostics.Add(Create(destProp, destSymbol,
                $"'{sourceDisplay}' is '{st}', which can be null, and the destination is '{dt}', which cannot. "
                + "Declare the destination property nullable or the source property non-nullable"
                + origin switch
                {
                    Origin.Attribute => ", or mark the destination property [Ignore].",
                    Origin.Profile => $", or configure it with ForMember(d => d.{destProp.DisplayName}, o => o.MapFrom(...)) or with o.Ignore().",
                    _ => ". " + ReverseHint(destProp),
                }, location));
            return new NestedObjectMapping(method, NullHandling.NotNull, default, skipped: true, updatesInPlace: false,
                sourceTypeFqn: string.Empty);
        }

        var source = sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var dest = destType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        // A struct is copied by the getter: updating the copy would change nothing, so it is assigned.
        bool readable = !destType.IsValueType && destProp.Property?.GetMethod is { } getter
                       && compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly);
        if (IsMappedByExistingMethod(compilation, method, source, dest))
        {
            return new NestedObjectMapping(method, nullHandling, default, skipped: false,
                updatesInPlace: readable && IsUpdatedByExistingMethod(compilation, method, source, dest),
                sourceTypeFqn: SymbolHelpers.GetFullyQualifiedName(source));
        }

        string ss = Display(source);
        string ds = Display(dest);
        diagnostics.Add(MissingPair(destProp, destSymbol,
            $"'{sourceDisplay}' is '{st}' and the destination is '{dt}', and there is no mapping from '{ss}' to '{ds}'. "
            + AddMappingHint(ss, ds), location, source, dest, origin));
        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CyclicMapping, destProp.Name, destSymbol.ToDisplayString())
            .WithLocation(location)
            .WithProperty(CyclePropertyKey, destProp.Name));
        return new NestedObjectMapping(method, nullHandling, ElementPairKeys(source, dest), skipped: false, updatesInPlace: readable,
            sourceTypeFqn: SymbolHelpers.GetFullyQualifiedName(source));
    }

    private static string AddMappingHint(string source, string dest)
        => $"Add [MapTo(typeof({dest}))] to '{source}', [MapFrom(typeof({source}))] to '{dest}', or CreateMap<{source}, {dest}>() in a profile.";

    /// <summary>
    /// IM0002 "no mapping from <paramref name="source"/> to <paramref name="dest"/>", reported only when the plan
    /// generates none of the pairs; with the properties of the code fix that adds the mapping.
    /// </summary>
    private static DiagnosticInfo MissingPair(DestinationMember destProp, INamedTypeSymbol destSymbol, string reason,
        LocationInfo? location, ITypeSymbol source, ITypeSymbol dest, Origin origin)
        => Create(destProp, destSymbol, reason, location)
            .WithProperty(MissingElementPairsKey, string.Join("|", ElementPairKeys(source, dest)))
            .WithProperty(PairSourceIdKey, DeclarationId(source))
            .WithProperty(PairDestinationIdKey, DeclarationId(dest))
            .WithProperty(PairSourceTypeKey, SymbolHelpers.GetFullyQualifiedName(source))
            .WithProperty(PairDestinationTypeKey, SymbolHelpers.GetFullyQualifiedName(dest))
            .WithProperty(MappingOriginKey, origin.ToString());

    // T:App.Address for a type the code fix can find (DocumentationCommentId); empty for a constructed generic type.
    private static string DeclarationId(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true }
            ? string.Empty
            : DocumentationCommentId.CreateDeclarationId(type) ?? string.Empty;

    /// <summary>
    /// The pair keys (<c>{source}-&gt;{dest}</c>, as <see cref="MappingDescriptor.PairKey"/>) under which a mapping of this
    /// compilation maps an element: the source element type, its base types and its interfaces (the generated call
    /// <c>item.MapToX()</c> binds to the mapping of a base type too).
    /// </summary>
    public static ImmutableArray<string> ElementPairKeys(ITypeSymbol sourceElement, ITypeSymbol destElement)
    {
        string dest = SymbolHelpers.GetFullyQualifiedName(destElement);
        var builder = ImmutableArray.CreateBuilder<string>();
        for (var type = sourceElement; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            builder.Add($"{SymbolHelpers.GetFullyQualifiedName(type)}->{dest}");
        }

        foreach (var iface in sourceElement.AllInterfaces)
        {
            builder.Add($"{SymbolHelpers.GetFullyQualifiedName(iface)}->{dest}");
        }

        return builder.ToImmutable();
    }

    private static DiagnosticInfo Create(DestinationMember destProp, INamedTypeSymbol destSymbol, string reason, LocationInfo? location)
        => new DiagnosticInfo(DiagnosticDescriptors.IncompatiblePropertyTypes, destProp.Name, destSymbol.ToDisplayString(), reason)
            .WithLocation(location)
            .WithProperty(MappingAnalyzer.PropertyNameKey, destProp.Name);

    private static string ReverseHint(DestinationMember destProp)
        => $"The reverse mapping of ReverseMap() maps by name only: declare it with CreateMap and configure '{destProp.DisplayName}' "
           + "there (ForMember with MapFrom, UseConverter or Ignore), or remove ReverseMap().";

    /// <summary>
    /// The generated <c>destination.X = value</c> compiles: an implicit conversion exists (identity, numeric widening,
    /// nullable wrapping, reference conversions, user-defined implicit operators). Nullable reference annotations do not
    /// matter here (<c>string?</c> → <c>string</c> converts; the nullable warning is item 3.5 of the plan).
    /// </summary>
    private static bool IsAssignable(Compilation compilation, ITypeSymbol source, ITypeSymbol dest)
        => SymbolHelpers.ContainsErrorType(source) || SymbolHelpers.ContainsErrorType(dest)
           || source.TypeKind == TypeKind.Dynamic   // converted at run time, as the compiler allows
           || compilation.ClassifyConversion(source, dest).IsImplicit;

    /// <summary>
    /// A class or struct that could have a mapping of its own (a "nested object"): not a primitive, string, enum, nullable
    /// value type, delegate, collection, or a type of the <c>System</c> namespaces (<c>Guid</c>, <c>DateTime</c>, …).
    /// With <paramref name="allowInterface"/> also an interface (the source type of a mapping may be one).
    /// </summary>
    private static bool IsObject(ITypeSymbol type, bool allowInterface = false)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct) && !(allowInterface && named.TypeKind == TypeKind.Interface))
        {
            return false;
        }

        if (named.SpecialType != SpecialType.None)
        {
            return false;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        if (SymbolHelpers.TryGetCollectionElementType(named, out _))
        {
            return false;
        }

        string? ns = named.ContainingNamespace?.ToDisplayString();
        return ns is null || (ns != "System" && !ns.StartsWith("System.", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// An accessible extension method <c>{method}</c> that the generated call <c>item.{method}()</c> binds to without a
    /// mapping of this compilation: the generated file declares no usings and is in <c>IronMapper.Generated</c>, so the
    /// compiler looks in that namespace, in <c>IronMapper</c> and in the global namespace, of every assembly (generated
    /// mappings of referenced assemblies; hand-written methods). The method takes <paramref name="sourceElement"/> (or a
    /// base type or interface of it) and returns a value assignable to <paramref name="destElement"/>. Without this check
    /// IM0002 would break a build that compiles with 2.0.0.
    /// </summary>
    private static bool IsMappedByExistingMethod(Compilation compilation, string method, ITypeSymbol sourceElement,
        ITypeSymbol destElement)
    {
        var ironMapper = compilation.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "IronMapper");
        var generated = ironMapper?.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Generated");
        foreach (var ns in new[] { generated, ironMapper, compilation.GlobalNamespace })
        {
            if (ns is null)
            {
                continue;
            }

            foreach (var type in ns.GetTypeMembers())
            {
                if (!type.MightContainExtensionMethods)
                {
                    continue;
                }

                foreach (var member in type.GetMembers(method))
                {
                    if (member is IMethodSymbol { IsExtensionMethod: true, Parameters.Length: 1, IsGenericMethod: false } candidate
                        && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly)
                        && IsReceiverConversion(compilation, sourceElement, candidate.Parameters[0].Type)
                        && compilation.ClassifyConversion(candidate.ReturnType, destElement).IsImplicit)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The in-place overload <c>{method}(this source, dest)</c> of an existing mapping (see <see cref="IsMappedByExistingMethod"/>,
    /// same namespaces): an accessible, non-generic extension method returning <c>void</c> that takes
    /// <paramref name="source"/> (or a base type or interface of it) and <paramref name="dest"/> (or a base type of it).
    /// </summary>
    private static bool IsUpdatedByExistingMethod(Compilation compilation, string method, ITypeSymbol source, ITypeSymbol dest)
    {
        var ironMapper = compilation.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "IronMapper");
        var generated = ironMapper?.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Generated");
        foreach (var ns in new[] { generated, ironMapper, compilation.GlobalNamespace })
        {
            if (ns is null)
            {
                continue;
            }

            foreach (var type in ns.GetTypeMembers())
            {
                if (!type.MightContainExtensionMethods)
                {
                    continue;
                }

                foreach (var member in type.GetMembers(method))
                {
                    if (member is IMethodSymbol { IsExtensionMethod: true, Parameters.Length: 2, IsGenericMethod: false, ReturnsVoid: true } candidate
                        && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly)
                        && IsReceiverConversion(compilation, source, candidate.Parameters[0].Type)
                        && IsReceiverConversion(compilation, dest, candidate.Parameters[1].Type))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // The conversions C# allows for the receiver of an extension method call: identity, implicit reference, boxing.
    private static bool IsReceiverConversion(Compilation compilation, ITypeSymbol receiver, ITypeSymbol parameter)
    {
        if (compilation is not CSharpCompilation csharp)
        {
            return SymbolEqualityComparer.Default.Equals(receiver, parameter);
        }

        var conversion = csharp.ClassifyConversion(receiver, parameter);
        return conversion.IsIdentity || (conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing));
    }

    private static string Display(ITypeSymbol type) => type.ToDisplayString();
}

/// <summary>Result of <see cref="PropertyTypeCheck.CheckNestedObject"/>; copied into <see cref="PropertyMappingDescriptor"/>.</summary>
internal sealed class NestedObjectMapping
{
    public NestedObjectMapping(string method, string nullHandling, ImmutableArray<string> pairs, bool skipped, bool updatesInPlace,
        string sourceTypeFqn)
    {
        SourceTypeFqn = sourceTypeFqn;
        UpdatesInPlace = updatesInPlace;
        Method = method;
        NullHandling = nullHandling;
        Pairs = pairs.IsDefault ? ImmutableArray<string>.Empty : pairs;
        Skipped = skipped;
    }

    /// <summary><c>MapTo{DestType}</c>, called as an extension method when no mapping of this compilation maps the pair.</summary>
    public string Method { get; }

    /// <summary>See <see cref="Analysis.NullHandling.ForNestedObject"/>.</summary>
    public string NullHandling { get; }

    /// <summary>
    /// The type pairs (the source type and its base types and interfaces → the destination type) of which this compilation
    /// must generate one; empty when a referenced assembly maps the pair.
    /// </summary>
    public ImmutableArray<string> Pairs { get; }

    /// <summary>IM0002 that no mapping can fix (nullable source, non-nullable destination): the property is not generated.</summary>
    public bool Skipped { get; }

    /// <summary>See <see cref="PropertyMappingDescriptor.NestedUpdatesInPlace"/>.</summary>
    public bool UpdatesInPlace { get; }

    /// <summary>See <see cref="PropertyMappingDescriptor.NestedSourceTypeFqn"/>.</summary>
    public string SourceTypeFqn { get; }
}

/// <summary>
/// The destination member a value is written to: a property (<c>destination.X = value</c>, an initializer line) or a
/// constructor parameter (<c>new D(x: value)</c>). The checks of <see cref="PropertyTypeCheck"/> take either.
/// </summary>
internal readonly struct DestinationMember
{
    private DestinationMember(string name, string displayName, ITypeSymbol type, IPropertySymbol? property)
    {
        Name = name;
        DisplayName = displayName;
        Type = type;
        Property = property;
    }

    /// <summary>The property or parameter name: the diagnostic's first argument and the name in the generated code.</summary>
    public string Name { get; }

    /// <summary>
    /// The name in the hints of a message (<c>ForMember(d =&gt; d.{DisplayName}, …)</c>): the property, or for a parameter
    /// the property of the same name, which <c>ForMember</c> configures (the parameter name when there is none).
    /// </summary>
    public string DisplayName { get; }

    /// <summary>The type the value must convert to.</summary>
    public ITypeSymbol Type { get; }

    /// <summary>The property; <see langword="null"/> for a constructor parameter (no in-place update of it).</summary>
    public IPropertySymbol? Property { get; }

    /// <summary>A constructor parameter; <paramref name="sameNamedProperty"/> is the property of the same name, if any.</summary>
    public static DestinationMember ForParameter(IParameterSymbol parameter, IPropertySymbol? sameNamedProperty)
        => new(parameter.Name, sameNamedProperty?.Name ?? parameter.Name, parameter.Type, null);

    /// <summary>A property.</summary>
    public static DestinationMember ForProperty(IPropertySymbol property)
        => new(property.Name, property.Name, property.Type, property);
}
