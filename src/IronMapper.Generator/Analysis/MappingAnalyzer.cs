using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.Generator.Analysis;

internal static class MappingAnalyzer
{
    // Fully-qualified attribute names used for matching on property-level attributes.
    private const string MapPropertyAttributeFqn = "IronMapper.Attributes.MapPropertyAttribute";
    private const string IgnoreAttributeFqn = "IronMapper.Attributes.IgnoreAttribute";
    private const string MapConverterAttributeFqn = "IronMapper.Attributes.MapConverterAttribute";

    /// <summary>Diagnostic property of IM0006 read by its code fix: the destination property names, separated by ';'.</summary>
    internal const string DestinationPropertiesKey = "DestinationProperties";

    /// <summary>Diagnostic property of IM0001: the name of the unmapped destination property.</summary>
    internal const string PropertyNameKey = "PropertyName";

    // -----------------------------------------------------------------------
    // Entry points called from the generator pipeline
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts <see cref="MappingDescriptor"/>s for a class decorated with one or more
    /// <c>[MapTo(typeof(TDest))]</c> attributes.
    /// Returns one descriptor per attribute.
    /// </summary>
    public static EquatableArray<MappingDescriptor> ExtractFromMapTo(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol sourceSymbol)
        {
            return ImmutableArray<MappingDescriptor>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        int index = 0;
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0)
            {
                continue;
            }

            var location = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax(ct));
            var destSymbol = GetMappedType(attr, "MapTo", sourceSymbol, location, builder);
            if (destSymbol is null)
            {
                continue;
            }

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ctx.SemanticModel.Compilation, location, ct)?.WithOrigin(
                CreateAttributeOrigin("MapTo", sourceSymbol, destSymbol, index++),
                $"[MapTo] on '{sourceSymbol.ToDisplayString()}'",
                originRank: 2,
                SymbolHelpers.GetDestinationNaming(destSymbol),
                location).WithMethodOverrides(GetMethodOverrides(attr));
            if (descriptor is not null)
            {
                builder.Add(descriptor);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts <see cref="MappingDescriptor"/>s for a class decorated with one or more
    /// <c>[MapFrom(typeof(TSource))]</c> attributes.
    /// Returns one descriptor per attribute.
    /// </summary>
    public static EquatableArray<MappingDescriptor> ExtractFromMapFrom(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol destSymbol)
        {
            return ImmutableArray<MappingDescriptor>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        int index = 0;
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0)
            {
                continue;
            }

            var location = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax(ct));
            var sourceSymbol = GetMappedType(attr, "MapFrom", destSymbol, location, builder);
            if (sourceSymbol is null)
            {
                continue;
            }

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ctx.SemanticModel.Compilation, location, ct)?.WithOrigin(
                CreateAttributeOrigin("MapFrom", sourceSymbol, destSymbol, index++),
                $"[MapFrom] on '{destSymbol.ToDisplayString()}'",
                originRank: 3,
                SymbolHelpers.GetDestinationNaming(destSymbol),
                location).WithMethodOverrides(GetMethodOverrides(attr));
            if (descriptor is not null)
            {
                builder.Add(descriptor);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The type in <c>typeof(...)</c> of a [MapTo]/[MapFrom] attribute, or <see langword="null"/> when there is no mapping
    /// to generate: a type that does not resolve (<c>typeof(Missing)</c>) is skipped without a diagnostic, because the
    /// compiler reports it at the attribute; a type that resolves but cannot be mapped (open generic, array, pointer)
    /// gets IM0003, which the compiler does not report (the mapping would otherwise be dropped silently).
    /// </summary>
    private static INamedTypeSymbol? GetMappedType(AttributeData attr, string attribute, INamedTypeSymbol annotated,
        LocationInfo? location, ImmutableArray<MappingDescriptor>.Builder builder)
    {
        if (attr.ConstructorArguments[0].Value is not ITypeSymbol type)
        {
            return null;
        }

        string problem;
        // Checked before the error types: the type arguments of typeof(Box<>) are error types.
        if (type is INamedTypeSymbol { IsUnboundGenericType: true })
        {
            problem = "an open generic type cannot be mapped; use a constructed type such as typeof(Box<int>)";
        }
        else if (SymbolHelpers.ContainsErrorType(type))
        {
            return null;
        }
        else if (type is INamedTypeSymbol named)
        {
            return named;
        }
        else
        {
            problem = "only classes, structs and records can be mapped";
        }

        builder.Add(CreateDiagnosticOnlyDescriptor(ImmutableArray.Create(new DiagnosticInfo(
            DiagnosticDescriptors.UnsupportedAttributeType, attribute, type.ToDisplayString(), annotated.ToDisplayString(), problem)
            .WithLocation(location))));
        return null;
    }

    /// <summary>
    /// <c>GenerateInPlace</c>/<c>GenerateList</c>/<c>GenerateArray</c> set in the attribute usage; a property that is not
    /// set stays <see langword="null"/>, so the value for the assembly applies (the property's getter cannot tell).
    /// </summary>
    private static GeneratedMethodOverrides GetMethodOverrides(AttributeData attr)
    {
        bool? inPlace = null, list = null, array = null;
        foreach (var argument in attr.NamedArguments)
        {
            if (argument.Value.Value is not bool value)
            {
                continue;
            }

            switch (argument.Key)
            {
                case "GenerateInPlace": inPlace = value; break;
                case "GenerateList": list = value; break;
                case "GenerateArray": array = value; break;
            }
        }

        return new GeneratedMethodOverrides(inPlace, list, array);
    }

    private static string CreateAttributeOrigin(string attribute, INamedTypeSymbol source, INamedTypeSymbol dest, int index)
        => $"attr:{attribute}:{SymbolHelpers.GetFullyQualifiedName(source)}->{SymbolHelpers.GetFullyQualifiedName(dest)}#{index:D4}";

    // -----------------------------------------------------------------------
    // Core analysis logic
    // -----------------------------------------------------------------------

    /// <param name="sourceSymbol">The source type.</param>
    /// <param name="destSymbol">The destination type.</param>
    /// <param name="compilation">The compilation (converter checks).</param>
    /// <param name="origin">The [MapTo]/[MapFrom] attribute: where diagnostics of the whole mapping point.</param>
    /// <param name="ct">Cancellation.</param>
    private static MappingDescriptor? BuildDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        Compilation compilation,
        LocationInfo? origin,
        CancellationToken ct)
    {
        // IM0010: generated methods cannot reference private/protected nested types.
        string? methodAccessibility = SymbolHelpers.GetGeneratedMethodAccessibility(
            sourceSymbol, destSymbol, out var inaccessibleType);
        if (methodAccessibility is null)
        {
            return CreateInaccessibleTypeDescriptor(sourceSymbol, destSymbol, inaccessibleType!, origin);
        }

        string sourceFullName = SymbolHelpers.GetFullyQualifiedName(sourceSymbol);
        string destFullName = SymbolHelpers.GetFullyQualifiedName(destSymbol);

        var sourceProps = SymbolHelpers.GetPublicReadableProperties(sourceSymbol);
        var destProps = SymbolHelpers.GetPublicSettableProperties(destSymbol);

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // IM0008: destination type is abstract or an interface.
        if (destSymbol.IsAbstract || destSymbol.TypeKind == TypeKind.Interface)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AbstractDestinationType,
                destSymbol.Name).WithLocation(origin));
            return new MappingDescriptor(
                sourceTypeName: sourceSymbol.Name,
                sourceNamespace: SymbolHelpers.GetNamespace(sourceSymbol),
                destTypeName: destSymbol.Name,
                destNamespace: SymbolHelpers.GetNamespace(destSymbol),
                propertyMappings: propertyMappings.ToImmutable(),
                diagnostics: diagnostics.ToImmutable(),
                hasCustomConverter: false,
                sourceFullName: sourceFullName,
                destFullName: destFullName,
                methodAccessibility: methodAccessibility);
        }

        // Index source props by name for fast case-insensitive lookup.
        var sourcePropsByName = new Dictionary<string, IPropertySymbol>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in sourceProps)
        {
            sourcePropsByName[p.Name] = p;
        }

        // Build reverse index: destination property name → source property (via [MapProperty]), and where each
        // [MapProperty] is (IM0006 points to it).
        var sourceByDestName = new Dictionary<string, IPropertySymbol>(
            System.StringComparer.OrdinalIgnoreCase);
        var mapPropertyLocations = new Dictionary<string, LocationInfo?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var sourceProp in sourceProps)
        {
            ct.ThrowIfCancellationRequested();
            string? destName = GetMapPropertyDestName(sourceProp, out var mapProperty);
            if (destName is not null)
            {
                sourceByDestName[destName] = sourceProp;
                mapPropertyLocations[destName] = LocationInfo.From(mapProperty?.ApplicationSyntaxReference?.GetSyntax(ct));
            }
        }

        var converters = new ConverterAnalyzer(compilation, sourceSymbol, destSymbol);

        // The value of a source property for a destination member (a property or a constructor parameter): a converter,
        // a collection mapped element by element, a nested object mapped by the mapping of its pair, or the value as is.
        // IM0002 (a value the generated code cannot take) goes to `into` and sets `unusable` to its reason. Without
        // `commit` (constructors being compared) the converter is not recorded.
        PropertyMappingDescriptor MapSourceProperty(IPropertySymbol sourceProp, DestinationMember target, bool isInitOnly,
            LocationInfo? location, ImmutableArray<DiagnosticInfo>.Builder into, bool commit, out string? unusable)
        {
            unusable = null;
            var collection = SymbolHelpers.DetectCollectionMapping(sourceProp.Type, target.Type);
            bool isIgnored = HasIgnoreAttribute(sourceProp);
            // An ignored property does not use its converter, so the converter is not checked.
            var converter = isIgnored ? null
                : commit ? GetConverter(sourceProp, converters, ct)
                : HasConverter(sourceProp) ? new ConverterUse(string.Empty, isCached: false) : null;
            // A nested object is mapped by the mapping of its type pair. IM0002: a value the generated assignment cannot
            // take is not generated (as if ignored), instead of a CS0029/CS0266/CS1061 in the generated file.
            var mappingPairs = default(EquatableArray<string>);
            NestedObjectMapping? nested = null;
            bool forgive = false;
            string sourceDisplay = $"{sourceSymbol.Name}.{sourceProp.Name}";
            // IM0023: a nullable source property for a converter that takes a non-nullable value.
            if (!isIgnored && converter is not null && GetConverterType(sourceProp) is { } converterType
                && PropertyTypeCheck.CheckNullableConverter(sourceProp.Type, converterType, target, destSymbol, sourceDisplay, location)
                    is { } nullableConverter)
            {
                into.Add(nullableConverter);
                forgive = true;
            }

            if (!isIgnored && converter is null)
            {
                if (collection is not null)
                {
                    if (CheckCollection(compilation, collection, sourceProp, target, sourceDisplay, destSymbol,
                            PropertyTypeCheck.Origin.Attribute, location, out mappingPairs) is { } missing)
                    {
                        into.Add(missing);
                    }

                    if (CheckNullableElements(collection, sourceProp, target, destSymbol, sourceDisplay, mappingPairs, location)
                        is { } nullableElements)
                    {
                        into.Add(nullableElements);
                        forgive = true;
                    }
                }
                else if ((nested = PropertyTypeCheck.CheckNestedObject(compilation, sourceProp.Type, target.Type,
                              sourceDisplay, target, destSymbol, PropertyTypeCheck.Origin.Attribute, location, into)
                          ?? PropertyTypeCheck.CheckValueConversion(compilation, sourceProp.Type, target.Type,
                              sourceDisplay, target, destSymbol, PropertyTypeCheck.Origin.Attribute, location, into)) is not null)
                {
                    isIgnored = nested.Skipped;
                    mappingPairs = nested.Pairs;
                    if (nested.Skipped)
                    {
                        unusable = into[into.Count - 1].MessageArgs[2];
                    }
                }
                else if (PropertyTypeCheck.CheckAssignment(compilation, sourceProp.Type, target.Type, sourceDisplay, target,
                             destSymbol, PropertyTypeCheck.Origin.Attribute, location) is { } incompatible)
                {
                    into.Add(incompatible);
                    isIgnored = true;
                    unusable = incompatible.MessageArgs[2];
                }
                // IM0023: assigned as it is, a value that can be null for a non-nullable member.
                else if (PropertyTypeCheck.CheckNullableValue(sourceProp.Type, target, destSymbol, sourceDisplay, $"s.{sourceProp.Name}",
                             PropertyTypeCheck.Origin.Attribute, location, out forgive) is { } narrowing)
                {
                    into.Add(narrowing);
                }
            }

            return new PropertyMappingDescriptor(
                sourcePropertyName: sourceProp.Name,
                destPropertyName: target.Name,
                isIgnored: isIgnored,
                converterType: converter?.FullName,
                needsNullCheck: sourceProp.Type.IsReferenceType,
                lambdaBody: null,
                isInitOnly: isInitOnly,
                collection: collection,
                converterIsCached: converter?.IsCached ?? false,
                mappingPairs: mappingPairs,
                nested: nested is { Skipped: false } ? nested : null,
                nullForgiving: forgive);
        }

        // A destination without an accessible parameterless constructor is created by a constructor (issue #24): its
        // parameters take the source properties as the destination properties do ([MapProperty], then the name ignoring
        // case); [Ignore] on the destination property of the same name, or on the source property, leaves the default value.
        var construction = ConstructorSelection.Choose(compilation, sourceSymbol, destSymbol,
            (parameter, sameNamed, commit) =>
            {
                if (sameNamed is not null && HasIgnoreAttribute(sameNamed))
                {
                    return ParameterValue.Ignored($"[Ignore] on '{destSymbol.Name}.{sameNamed.Name}'");
                }

                if (!sourceByDestName.TryGetValue(parameter.Name, out var sourceProp)
                    && !sourcePropsByName.TryGetValue(parameter.Name, out sourceProp))
                {
                    return ParameterValue.NoSource();
                }

                if (HasIgnoreAttribute(sourceProp))
                {
                    return ParameterValue.Ignored($"[Ignore] on '{sourceSymbol.Name}.{sourceProp.Name}'");
                }

                var found = ImmutableArray.CreateBuilder<DiagnosticInfo>();
                var mapping = MapSourceProperty(sourceProp, DestinationMember.ForParameter(parameter, sameNamed),
                    isInitOnly: false, LocationInfo.From(parameter) ?? origin, found, commit, out string? unusable);
                return unusable is null ? ParameterValue.Mapped(mapping, found.ToImmutable()) : ParameterValue.Unusable(unusable);
            },
            origin, atParameter: true,
            (parameter, _) => $"Add the property to '{sourceSymbol.Name}', or put [MapProperty(\"{parameter.Name}\")] on the "
                              + "source property that holds the value.");
        diagnostics.AddRange(construction.Diagnostics);

        // Build set of destination property names for IM0006 validation: the settable properties and the parameters of
        // the constructor that creates the destination.
        var destPropNames = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in destProps)
        {
            destPropNames.Add(p.Name);
        }

        destPropNames.UnionWith(construction.SetByConstructor);

        // IM0006: validate that every [MapProperty] destination name actually exists. The code fix suggests one of
        // the destination properties, passed as a diagnostic property.
        foreach (var kv in sourceByDestName)
        {
            ct.ThrowIfCancellationRequested();
            if (!destPropNames.Contains(kv.Key))
            {
                diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MapPropertyDestinationNotFound,
                        kv.Key,
                        destSymbol.Name)
                    .WithLocation(mapPropertyLocations[kv.Key] ?? origin)
                    .WithProperty(DestinationPropertiesKey, string.Join(";", destProps.Select(p => p.Name))));
            }
        }

        // No constructor can be called (IM0009, IM0017, IM0018): the mapping is not generated.
        if (construction.Failed)
        {
            return CreateDiagnosticOnlyDescriptor(diagnostics.ToImmutable());
        }

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            // Destination property explicitly ignored via [Ignore], or set by the constructor — skip silently.
            if (HasIgnoreAttribute(destProp) || construction.SetByConstructor.Contains(destProp.Name))
            {
                continue;
            }

            bool isInitOnly = destProp.SetMethod?.IsInitOnly ?? false;

            // Priority 1: explicit [MapProperty("DestName")] on a source property.
            // Priority 2: matching name (case-insensitive).
            if (sourceByDestName.TryGetValue(destProp.Name, out var sourceProp)
                || sourcePropsByName.TryGetValue(destProp.Name, out sourceProp))
            {
                propertyMappings.Add(MapSourceProperty(sourceProp, DestinationMember.ForProperty(destProp), isInitOnly, LocationInfo.From(destProp) ?? origin,
                    diagnostics, commit: true, out _));
                continue;
            }

            // Priority 3: no match → IM0001 warning, at the destination property (the code fix adds [Ignore] there),
            // or at the attribute when the property is not in source (a type from another assembly).
            diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.UnmappedDestinationProperty,
                    destProp.Name,
                    destSymbol.Name,
                    sourceSymbol.Name)
                .WithLocation(LocationInfo.From(destProp) ?? origin)
                .WithProperty(PropertyNameKey, destProp.Name));
        }

        // A converter type that did not resolve: the compiler reports it at [MapConverter]; skip the mapping.
        if (converters.HasUnresolvedType)
        {
            return null;
        }

        // A converter that the generated code cannot create (IM0015): the mapping is not generated.
        if (!converters.CanGenerate)
        {
            diagnostics.AddRange(converters.Diagnostics);
            return CreateDiagnosticOnlyDescriptor(diagnostics.ToImmutable());
        }

        return new MappingDescriptor(
            sourceTypeName: sourceSymbol.Name,
            sourceNamespace: SymbolHelpers.GetNamespace(sourceSymbol),
            destTypeName: destSymbol.Name,
            destNamespace: SymbolHelpers.GetNamespace(destSymbol),
            propertyMappings: propertyMappings.ToImmutable(),
            diagnostics: diagnostics.ToImmutable(),
            hasCustomConverter: HasAnyConverter(propertyMappings) || construction.Arguments.Any(a => a.ConverterType is not null),
            sourceFullName: sourceFullName,
            destFullName: destFullName,
            methodAccessibility: methodAccessibility,
            usesConstructor: construction.UsesConstructor,
            constructorArguments: construction.Arguments);
    }

    /// <summary>
    /// Builds a diagnostic-only descriptor (empty <see cref="MappingDescriptor.SourceTypeName"/>, so no
    /// source is emitted) carrying IM0010 for a mapping whose types the generated class cannot access.
    /// </summary>
    internal static MappingDescriptor CreateInaccessibleTypeDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        INamedTypeSymbol inaccessibleType,
        LocationInfo? origin)
        => CreateDiagnosticOnlyDescriptor(ImmutableArray.Create(new DiagnosticInfo(
            DiagnosticDescriptors.InaccessibleMappingType,
            sourceSymbol.ToDisplayString(),
            destSymbol.ToDisplayString(),
            inaccessibleType.ToDisplayString(),
            SymbolHelpers.ToKeyword(inaccessibleType.DeclaredAccessibility)).WithLocation(origin)));

    /// <summary>
    /// Builds a descriptor that only reports <paramref name="diagnostics"/> and emits no source
    /// (empty <see cref="MappingDescriptor.SourceTypeName"/>).
    /// </summary>
    internal static MappingDescriptor CreateDiagnosticOnlyDescriptor(EquatableArray<DiagnosticInfo> diagnostics)
        => new(
            sourceTypeName: string.Empty,
            sourceNamespace: null,
            destTypeName: string.Empty,
            destNamespace: null,
            propertyMappings: ImmutableArray<PropertyMappingDescriptor>.Empty,
            diagnostics: diagnostics,
            hasCustomConverter: false);

    // -----------------------------------------------------------------------
    // Symbol helpers
    // -----------------------------------------------------------------------

    private static string? GetMapPropertyDestName(IPropertySymbol prop, out AttributeData? attribute)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == MapPropertyAttributeFqn
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string destName)
            {
                attribute = attr;
                return destName;
            }
        }
        attribute = null;
        return null;
    }

    internal static bool HasIgnoreAttribute(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == IgnoreAttributeFqn)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The converter of <c>[MapConverter]</c> on <paramref name="prop"/>, checked by <paramref name="converters"/>
    /// (fully qualified, so nested and generic converter types work: <c>global::App.Outer.Converter</c>).
    /// </summary>
    private static ConverterUse? GetConverter(IPropertySymbol prop, ConverterAnalyzer converters, CancellationToken ct)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == MapConverterAttributeFqn
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol converterSymbol)
            {
                // IM0015 points to the type in typeof(...), or to the attribute.
                var syntax = attr.ApplicationSyntaxReference?.GetSyntax(ct);
                var type = (syntax as AttributeSyntax)?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf
                    ? typeOf.Type
                    : syntax;
                return converters.Add(
                    converterSymbol, $"[MapConverter] on '{prop.ContainingType.ToDisplayString()}.{prop.Name}'",
                    LocationInfo.From(type), ct);
            }
        }
        return null;
    }

    /// <summary>The converter type of <c>[MapConverter]</c> on <paramref name="prop"/>, or <see langword="null"/>.</summary>
    private static INamedTypeSymbol? GetConverterType(IPropertySymbol prop)
        => prop.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == MapConverterAttributeFqn && a.ConstructorArguments.Length > 0)
            .Select(a => a.ConstructorArguments[0].Value as INamedTypeSymbol)
            .FirstOrDefault(t => t is not null);

    /// <summary>
    /// IM0023 of a collection property mapped element by element whose source elements are a nullable reference type
    /// (<see cref="PropertyTypeCheck.CheckNullableElements"/>); reported only when one of <paramref name="mappingPairs"/> is
    /// generated (or a referenced assembly maps the elements).
    /// </summary>
    internal static DiagnosticInfo? CheckNullableElements(CollectionMapping collection, IPropertySymbol sourceProp,
        DestinationMember destProp, INamedTypeSymbol destSymbol, string sourceDisplay, EquatableArray<string> mappingPairs,
        LocationInfo? location)
        => SymbolHelpers.TryGetCollectionElementType(sourceProp.Type, out var sourceElement) && sourceElement is not null
            ? PropertyTypeCheck.CheckNullableElements(sourceElement, collection.ElementMethod, destProp, destSymbol, sourceDisplay,
                mappingPairs.AsImmutableArray(), location)
            : null;

    /// <summary><c>[MapConverter]</c> on <paramref name="prop"/>, without checking the converter (constructors being compared).</summary>
    private static bool HasConverter(IPropertySymbol prop)
        => prop.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == MapConverterAttributeFqn && a.ConstructorArguments.Length > 0);

    /// <summary>
    /// IM0002 of a collection property mapped element by element (<see cref="PropertyTypeCheck.CheckCollection"/>): the
    /// diagnostic, reported only when the plan generates no mapping of the element types, and those pairs
    /// (<paramref name="mappingPairs"/>, empty when a referenced assembly maps the elements).
    /// </summary>
    internal static DiagnosticInfo? CheckCollection(Compilation compilation, CollectionMapping collection,
        IPropertySymbol sourceProp, DestinationMember destProp, string sourceDisplay, INamedTypeSymbol destSymbol,
        PropertyTypeCheck.Origin origin, LocationInfo? location, out EquatableArray<string> mappingPairs)
    {
        mappingPairs = default;
        if (!SymbolHelpers.TryGetCollectionElementType(sourceProp.Type, out var sourceElement) || sourceElement is null
            || !SymbolHelpers.TryGetCollectionElementType(destProp.Type, out var destElement) || destElement is null)
        {
            return null;
        }

        var diagnostic = PropertyTypeCheck.CheckCollection(compilation, collection, sourceElement, destElement, sourceDisplay,
            destProp, destSymbol, origin, location, out bool mappedElsewhere);
        if (!mappedElsewhere)
        {
            mappingPairs = PropertyTypeCheck.ElementPairKeys(sourceElement, destElement);
        }

        return diagnostic;
    }

    private static bool HasAnyConverter(ImmutableArray<PropertyMappingDescriptor>.Builder mappings)
    {
        foreach (var m in mappings)
        {
            if (m.ConverterType is not null)
            {
                return true;
            }
        }

        return false;
    }
}
