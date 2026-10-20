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
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol destSymbol) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ct)?.WithOrigin(
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
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol sourceSymbol) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ct)?.WithOrigin(
                CreateAttributeOrigin("MapFrom", sourceSymbol, destSymbol, index++),
                $"[MapFrom] on '{destSymbol.ToDisplayString()}'",
                originRank: 3,
                SymbolHelpers.GetDestinationNaming(destSymbol));
            if (descriptor is not null)
                builder.Add(descriptor);
        }

        return builder.ToImmutable();
    }

    private static string CreateAttributeOrigin(string attribute, INamedTypeSymbol source, INamedTypeSymbol dest, int index)
        => $"attr:{attribute}:{SymbolHelpers.GetFullyQualifiedName(source)}->{SymbolHelpers.GetFullyQualifiedName(dest)}#{index:D4}";

    // -----------------------------------------------------------------------
    // Core analysis logic
    // -----------------------------------------------------------------------

    private static MappingDescriptor? BuildDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
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

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            // Destination property explicitly ignored via [Ignore] — skip silently.
            if (HasIgnoreAttribute(destProp)) continue;

            bool isInitOnly = destProp.SetMethod?.IsInitOnly ?? false;

            // Priority 1: explicit [MapProperty("DestName")] on a source property.
            if (sourceByDestName.TryGetValue(destProp.Name, out var mappedProp))
            {
                var collection = SymbolHelpers.DetectCollectionMapping(mappedProp.Type, destProp.Type);
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: mappedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: HasIgnoreAttribute(mappedProp),
                    converterType: GetConverterTypeName(mappedProp),
                    needsNullCheck: mappedProp.Type.IsReferenceType,
                    lambdaBody: null,
                    isInitOnly: isInitOnly,
                    collection: collection));
                continue;
            }

            // Priority 2: matching name (case-insensitive).
            if (sourcePropsByName.TryGetValue(destProp.Name, out var namedProp))
            {
                var collection = SymbolHelpers.DetectCollectionMapping(namedProp.Type, destProp.Type);
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: namedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: HasIgnoreAttribute(namedProp),
                    converterType: GetConverterTypeName(namedProp),
                    needsNullCheck: namedProp.Type.IsReferenceType,
                    lambdaBody: null,
                    isInitOnly: isInitOnly,
                    collection: collection));
                continue;
            }

            // Priority 3: no match → IM0001 warning.
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.UnmappedDestinationProperty,
                destProp.Name,
                destSymbol.Name,
                sourceSymbol.Name));
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

    private static string? GetConverterTypeName(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == MapConverterAttributeFqn
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol converterSymbol)
            {
                // Fully qualified, so nested and generic converter types work (global::App.Outer.Converter).
                return SymbolHelpers.GetFullyQualifiedName(converterSymbol);
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
