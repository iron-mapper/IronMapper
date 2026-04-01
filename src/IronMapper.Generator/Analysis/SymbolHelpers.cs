using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Shared Roslyn symbol utilities used by both <see cref="MappingAnalyzer"/> and
/// <see cref="ProfileAnalyzer"/>.
/// </summary>
internal static class SymbolHelpers
{
    /// <summary>
    /// Returns all public, non-static, non-indexer properties that have a getter,
    /// walking up the inheritance chain (excluding <c>System.Object</c>).
    /// </summary>
    public static IReadOnlyList<IPropertySymbol> GetPublicReadableProperties(INamedTypeSymbol type)
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

    /// <summary>
    /// Returns all public, non-static, non-indexer properties that have a public setter,
    /// walking up the inheritance chain (excluding <c>System.Object</c>).
    /// </summary>
    public static IReadOnlyList<IPropertySymbol> GetPublicSettableProperties(INamedTypeSymbol type)
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

    /// <summary>
    /// Returns the fully-qualified namespace of <paramref name="symbol"/>,
    /// or <see langword="null"/> for types in the global namespace.
    /// </summary>
    public static string? GetNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? null : ns.ToDisplayString();
    }
}
