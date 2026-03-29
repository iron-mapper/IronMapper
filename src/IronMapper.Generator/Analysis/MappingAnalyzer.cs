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
    public static ImmutableArray<MappingDescriptor> ExtractFromMapTo(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol sourceSymbol)
            return ImmutableArray<MappingDescriptor>.Empty;

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol destSymbol) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ct);
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
    public static ImmutableArray<MappingDescriptor> ExtractFromMapFrom(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol destSymbol)
            return ImmutableArray<MappingDescriptor>.Empty;

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol sourceSymbol) continue;

            var descriptor = BuildDescriptor(sourceSymbol, destSymbol, ct);
            if (descriptor is not null)
                builder.Add(descriptor);
        }

        return builder.ToImmutable();
    }

    // -----------------------------------------------------------------------
    // Core analysis logic
    // -----------------------------------------------------------------------

    private static MappingDescriptor? BuildDescriptor(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        CancellationToken ct)
    {
        var sourceProps = GetPublicReadableProperties(sourceSymbol);
        var destProps = GetPublicSettableProperties(destSymbol);

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

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            // Priority 1: explicit [MapProperty("DestName")] on a source property.
            if (sourceByDestName.TryGetValue(destProp.Name, out var mappedProp))
            {
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: mappedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: HasIgnoreAttribute(mappedProp),
                    converterType: GetConverterTypeName(mappedProp),
                    needsNullCheck: mappedProp.Type.IsReferenceType));
                continue;
            }

            // Priority 2: matching name (case-insensitive).
            if (sourcePropsByName.TryGetValue(destProp.Name, out var namedProp))
            {
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: namedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: HasIgnoreAttribute(namedProp),
                    converterType: GetConverterTypeName(namedProp),
                    needsNullCheck: namedProp.Type.IsReferenceType));
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
            sourceNamespace: GetNamespace(sourceSymbol),
            destTypeName: destSymbol.Name,
            destNamespace: GetNamespace(destSymbol),
            propertyMappings: propertyMappings.ToImmutable(),
            diagnostics: diagnostics.ToImmutable(),
            hasCustomConverter: propertyMappings.Count > 0 && HasAnyConverter(propertyMappings));
    }

    // -----------------------------------------------------------------------
    // Symbol helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<IPropertySymbol> GetPublicReadableProperties(INamedTypeSymbol type)
    {
        var result = new List<IPropertySymbol>();
        var current = type;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.DeclaredAccessibility == Accessibility.Public
                    && !prop.IsStatic
                    && !prop.IsIndexer
                    && prop.GetMethod is not null)
                {
                    result.Add(prop);
                }
            }
            current = current.BaseType;
        }
        return result;
    }

    private static IReadOnlyList<IPropertySymbol> GetPublicSettableProperties(INamedTypeSymbol type)
    {
        var result = new List<IPropertySymbol>();
        var current = type;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.DeclaredAccessibility == Accessibility.Public
                    && !prop.IsStatic
                    && !prop.IsIndexer
                    && prop.SetMethod is { DeclaredAccessibility: Accessibility.Public })
                {
                    result.Add(prop);
                }
            }
            current = current.BaseType;
        }
        return result;
    }

    private static string? GetNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? null : ns.ToDisplayString();
    }

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
                var ns = GetNamespace(converterSymbol);
                return ns is null
                    ? converterSymbol.Name
                    : $"{ns}.{converterSymbol.Name}";
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
