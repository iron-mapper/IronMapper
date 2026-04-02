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

    // -----------------------------------------------------------------------
    // Collection type helpers
    // -----------------------------------------------------------------------

    private const string IEnumerableOfTFqn = "System.Collections.Generic.IEnumerable<T>";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is an array or implements
    /// <c>IEnumerable&lt;T&gt;</c>, and sets <paramref name="elementType"/> to the element type.
    /// </summary>
    public static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        // T[] — element type is direct
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            // Check if the type itself is IEnumerable<T>
            if (namedType.OriginalDefinition.ToDisplayString() == IEnumerableOfTFqn)
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            // Check implemented interfaces for IEnumerable<T>
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.IsGenericType
                    && iface.OriginalDefinition.ToDisplayString() == IEnumerableOfTFqn)
                {
                    elementType = iface.TypeArguments[0];
                    return true;
                }
            }
        }

        elementType = null;
        return false;
    }

    /// <summary>
    /// Returns the collection materialisation kind for the generated LINQ call:
    /// <c>"Array"</c> for arrays, <c>"List"</c> for <c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c>
    /// / <c>ICollection&lt;T&gt;</c>, or <c>"Enumerable"</c> for <c>IEnumerable&lt;T&gt;</c>
    /// and other types.
    /// </summary>
    public static string GetCollectionOutputType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return "Array";

        if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var def = namedType.OriginalDefinition.ToDisplayString();
            if (def == "System.Collections.Generic.List<T>"
                || def == "System.Collections.Generic.IList<T>"
                || def == "System.Collections.Generic.ICollection<T>"
                || def == "System.Collections.Generic.IReadOnlyList<T>"
                || def == "System.Collections.Generic.IReadOnlyCollection<T>")
                return "List";
        }

        return "Enumerable";
    }
}
