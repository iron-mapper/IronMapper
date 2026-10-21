using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Analysis;

internal static class MappingAnalyzer
{
    // Fully-qualified attribute names used for matching on property-level attributes.
    private const string MapPropertyAttributeFqn = "IronMapper.Attributes.MapPropertyAttribute";
    private const string IgnoreAttributeFqn = "IronMapper.Attributes.IgnoreAttribute";
    private const string MapConverterAttributeFqn = "IronMapper.Attributes.MapConverterAttribute";

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
            return ImmutableArray<MappingDescriptor>.Empty;

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        var index = 0;
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0) continue;
            var destSymbol = GetMappedType(attr, "MapTo", sourceSymbol, builder);
            if (destSymbol is null) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ctx.SemanticModel.Compilation, ct)?.WithOrigin(
                CreateAttributeOrigin("MapTo", sourceSymbol, destSymbol, index++),
                $"[MapTo] on '{sourceSymbol.ToDisplayString()}'",
                originRank: 2,
                SymbolHelpers.GetDestinationNaming(destSymbol));
            if (descriptor is not null)
                builder.Add(descriptor);
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
            return ImmutableArray<MappingDescriptor>.Empty;

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        var index = 0;
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0) continue;
            var sourceSymbol = GetMappedType(attr, "MapFrom", destSymbol, builder);
            if (sourceSymbol is null) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ctx.SemanticModel.Compilation, ct)?.WithOrigin(
                CreateAttributeOrigin("MapFrom", sourceSymbol, destSymbol, index++),
                $"[MapFrom] on '{destSymbol.ToDisplayString()}'",
                originRank: 3,
                SymbolHelpers.GetDestinationNaming(destSymbol));
            if (descriptor is not null)
                builder.Add(descriptor);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The type in <c>typeof(...)</c> of a [MapTo]/[MapFrom] attribute, or <see langword="null"/> when there is no mapping
    /// to generate: a type that does not resolve (<c>typeof(Missing)</c>) is skipped without a diagnostic, because the
    /// compiler reports it at the attribute; a type that resolves but cannot be mapped (open generic, array, pointer)
    /// gets IM0003, which the compiler does not report (the mapping would otherwise be dropped silently).
    /// </summary>
    private static INamedTypeSymbol? GetMappedType(
        AttributeData attr, string attribute, INamedTypeSymbol annotated, ImmutableArray<MappingDescriptor>.Builder builder)
    {
        if (attr.ConstructorArguments[0].Value is not ITypeSymbol type) return null;

        string problem;
        // Checked before the error types: the type arguments of typeof(Box<>) are error types.
        if (type is INamedTypeSymbol { IsUnboundGenericType: true })
            problem = "an open generic type cannot be mapped; use a constructed type such as typeof(Box<int>)";
        else if (SymbolHelpers.ContainsErrorType(type))
            return null;
        else if (type is INamedTypeSymbol named)
            return named;
        else
            problem = "only classes, structs and records can be mapped";

        builder.Add(CreateDiagnosticOnlyDescriptor(ImmutableArray.Create(new DiagnosticInfo(
            DiagnosticDescriptors.UnsupportedAttributeType, attribute, type.ToDisplayString(), annotated.ToDisplayString(), problem))));
        return null;
    }

    private static string CreateAttributeOrigin(string attribute, INamedTypeSymbol source, INamedTypeSymbol dest, int index)
        => $"attr:{attribute}:{SymbolHelpers.GetFullyQualifiedName(source)}->{SymbolHelpers.GetFullyQualifiedName(dest)}#{index:D4}";

    // -----------------------------------------------------------------------
    // Core analysis logic
    // -----------------------------------------------------------------------

    private static MappingDescriptor? BuildDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        Compilation compilation,
        CancellationToken ct)
    {
        // IM0010: generated methods cannot reference private/protected nested types.
        var methodAccessibility = SymbolHelpers.GetGeneratedMethodAccessibility(
            sourceSymbol, destSymbol, out var inaccessibleType);
        if (methodAccessibility is null)
            return CreateInaccessibleTypeDescriptor(sourceSymbol, destSymbol, inaccessibleType!);

        var sourceFullName = SymbolHelpers.GetFullyQualifiedName(sourceSymbol);
        var destFullName = SymbolHelpers.GetFullyQualifiedName(destSymbol);

        var sourceProps = SymbolHelpers.GetPublicReadableProperties(sourceSymbol);
        var destProps = SymbolHelpers.GetPublicSettableProperties(destSymbol);

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // IM0008: destination type is abstract or an interface.
        if (destSymbol.IsAbstract || destSymbol.TypeKind == TypeKind.Interface)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AbstractDestinationType,
                destSymbol.Name));
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
            sourcePropsByName[p.Name] = p;

        // Build reverse index: destination property name → source property (via [MapProperty]).
        var sourceByDestName = new Dictionary<string, IPropertySymbol>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var sourceProp in sourceProps)
        {
            ct.ThrowIfCancellationRequested();
            var destName = GetMapPropertyDestName(sourceProp);
            if (destName is not null)
                sourceByDestName[destName] = sourceProp;
        }

        // Build set of destination property names for IM0006 validation.
        var destPropNames = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in destProps)
            destPropNames.Add(p.Name);

        // IM0006: validate that every [MapProperty] destination name actually exists.
        foreach (var kv in sourceByDestName)
        {
            ct.ThrowIfCancellationRequested();
            if (!destPropNames.Contains(kv.Key))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MapPropertyDestinationNotFound,
                    kv.Key,
                    destSymbol.Name));
            }
        }

        // IM0009: for record destinations, warn about primary constructor parameters
        // that have no matching source property.
        if (destSymbol.IsRecord)
        {
            foreach (var ctor in destSymbol.Constructors)
            {
                ct.ThrowIfCancellationRequested();
                if (ctor.IsImplicitlyDeclared) continue;
                foreach (var param in ctor.Parameters)
                {
                    if (!sourcePropsByName.ContainsKey(param.Name))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.RecordParameterNotMapped,
                            param.Name,
                            destSymbol.Name));
                    }
                }
            }
        }

        var converters = new ConverterAnalyzer(compilation, sourceSymbol, destSymbol);

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            // Destination property explicitly ignored via [Ignore] — skip silently.
            if (HasIgnoreAttribute(destProp)) continue;

            bool isInitOnly = destProp.SetMethod?.IsInitOnly ?? false;

            // Priority 1: explicit [MapProperty("DestName")] on a source property.
            // Priority 2: matching name (case-insensitive).
            if (sourceByDestName.TryGetValue(destProp.Name, out var sourceProp)
                || sourcePropsByName.TryGetValue(destProp.Name, out sourceProp))
            {
                var collection = SymbolHelpers.DetectCollectionMapping(sourceProp.Type, destProp.Type);
                var isIgnored = HasIgnoreAttribute(sourceProp);
                // An ignored property does not use its converter, so the converter is not checked.
                var converter = isIgnored ? null : GetConverter(sourceProp, converters, ct);
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: sourceProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: isIgnored,
                    converterType: converter?.FullName,
                    needsNullCheck: sourceProp.Type.IsReferenceType,
                    lambdaBody: null,
                    isInitOnly: isInitOnly,
                    collection: collection,
                    converterIsCached: converter?.IsCached ?? false));
                continue;
            }

            // Priority 3: no match → IM0001 warning.
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.UnmappedDestinationProperty,
                destProp.Name,
                destSymbol.Name,
                sourceSymbol.Name));
        }

        // A converter type that did not resolve: the compiler reports it at [MapConverter]; skip the mapping.
        if (converters.HasUnresolvedType) return null;

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
            hasCustomConverter: propertyMappings.Count > 0 && HasAnyConverter(propertyMappings),
            sourceFullName: sourceFullName,
            destFullName: destFullName,
            methodAccessibility: methodAccessibility);
    }

    /// <summary>
    /// Builds a diagnostic-only descriptor (empty <see cref="MappingDescriptor.SourceTypeName"/>, so no
    /// source is emitted) carrying IM0010 for a mapping whose types the generated class cannot access.
    /// </summary>
    internal static MappingDescriptor CreateInaccessibleTypeDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        INamedTypeSymbol inaccessibleType)
        => CreateDiagnosticOnlyDescriptor(ImmutableArray.Create(new DiagnosticInfo(
            DiagnosticDescriptors.InaccessibleMappingType,
            sourceSymbol.ToDisplayString(),
            destSymbol.ToDisplayString(),
            inaccessibleType.ToDisplayString(),
            SymbolHelpers.ToKeyword(inaccessibleType.DeclaredAccessibility))));

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

    private static string? GetMapPropertyDestName(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == MapPropertyAttributeFqn
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string destName)
            {
                return destName;
            }
        }
        return null;
    }

    private static bool HasIgnoreAttribute(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == IgnoreAttributeFqn)
                return true;
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
                return converters.Add(
                    converterSymbol, $"[MapConverter] on '{prop.ContainingType.ToDisplayString()}.{prop.Name}'", ct);
            }
        }
        return null;
    }

    private static bool HasAnyConverter(ImmutableArray<PropertyMappingDescriptor>.Builder mappings)
    {
        foreach (var m in mappings)
            if (m.ConverterType is not null) return true;
        return false;
    }
}
