using System.Collections.Generic;
using IronMapper.Generator.Analysis.Models;
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

    /// <summary>
    /// Returns the fully-qualified C# name of <paramref name="type"/> (e.g.
    /// <c>global::App.Outer.Inner</c>, <c>global::App.Box&lt;global::App.Payload&gt;</c>).
    /// </summary>
    public static string GetFullyQualifiedName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>As <see cref="GetFullyQualifiedName"/>, with <c>?</c> on a nullable reference type (a declared parameter).</summary>
    public static string GetFullyQualifiedNullableName(ITypeSymbol type)
        => type.ToDisplayString(FullyQualifiedNullableFormat);

    /// <summary>
    /// Returns the parts of <paramref name="destType"/> used to qualify a colliding <c>MapTo{Name}</c> method name:
    /// namespace segments and containing types (outermost first) and the type arguments.
    /// </summary>
    public static DestinationNaming GetDestinationNaming(INamedTypeSymbol destType)
    {
        var segments = new List<string>();
        for (var containing = destType.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            segments.Insert(0, containing.Name);
        }

        for (var ns = destType.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
        {
            segments.Insert(0, ns.Name);
        }

        if (destType.TypeArguments.Length == 0)
        {
            return new DestinationNaming(string.Join(".", segments), string.Empty, string.Empty);
        }

        var shortArgs = new List<string>();
        var fullArgs = new List<string>();
        foreach (var argument in destType.TypeArguments)
        {
            shortArgs.Add(GetShortTypeName(argument));
            fullArgs.Add(ToIdentifier(GetFullyQualifiedName(argument)));
        }

        return new DestinationNaming(
            string.Join(".", segments),
            "Of" + string.Join("And", shortArgs),
            "Of" + string.Join("And", fullArgs));
    }

    /// <summary>
    /// Turns a type name into identifier characters: drops <c>global::</c> and replaces every character that is
    /// not a letter, digit or underscore with <c>_</c> (e.g. <c>global::App.Box&lt;int&gt;</c> → <c>App_Box_int_</c>).
    /// </summary>
    public static string ToIdentifier(string typeName)
    {
        if (typeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            typeName = typeName.Substring("global::".Length);
        }

        char[] chars = typeName.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    // Int32, StringArray, ListOfInt32, DictionaryOfStringAndInt32.
    private static string GetShortTypeName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => GetShortTypeName(array.ElementType) + "Array",
        INamedTypeSymbol { TypeArguments.Length: > 0 } named =>
            named.Name + "Of" + string.Join("And", System.Linq.Enumerable.Select(named.TypeArguments, GetShortTypeName)),
        _ => type.Name,
    };

    /// <summary>
    /// Determines the accessibility modifier for generated methods whose signatures use
    /// <paramref name="sourceType"/> and <paramref name="destType"/>: the minimum effective
    /// accessibility of both types, taking containing types and type arguments into account.
    /// </summary>
    /// <param name="sourceType">The mapping source type.</param>
    /// <param name="destType">The mapping destination type.</param>
    /// <param name="inaccessibleType">
    /// The first type (the mapped type itself, a containing type or a type argument) that the
    /// generated <c>GeneratedMappers</c> class cannot access; <see langword="null"/> when both types are accessible.
    /// </param>
    /// <returns><c>"public"</c> or <c>"internal"</c>; <see langword="null"/> when a type is inaccessible.</returns>
    public static string? GetGeneratedMethodAccessibility(
        ITypeSymbol sourceType,
        ITypeSymbol destType,
        out INamedTypeSymbol? inaccessibleType)
    {
        bool isInternal = false;
        inaccessibleType = FindInaccessibleType(sourceType, ref isInternal)
                           ?? FindInaccessibleType(destType, ref isInternal);
        if (inaccessibleType is not null)
        {
            return null;
        }

        return isInternal ? "internal" : "public";
    }

    /// <summary>
    /// True when <paramref name="type"/>, a type containing it, its element type or a type argument did not resolve
    /// (<c>typeof(Missing)</c>, an inaccessible or ambiguous type). The compiler already reports an error at the user's
    /// code; generated code that names such a type would only add a second error inside a <c>.g.cs</c> file.
    /// </summary>
    public static bool ContainsErrorType(ITypeSymbol type)
    {
        switch (type)
        {
            case { TypeKind: TypeKind.Error }:
                return true;
            case IArrayTypeSymbol array:
                return ContainsErrorType(array.ElementType);
            case INamedTypeSymbol named:
                if (named.ContainingType is not null && ContainsErrorType(named.ContainingType))
                {
                    return true;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (ContainsErrorType(argument))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>Returns the C# keyword(s) for <paramref name="accessibility"/>.</summary>
    public static string ToKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "not accessible",
    };

    private static INamedTypeSymbol? FindInaccessibleType(ITypeSymbol type, ref bool isInternal)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindInaccessibleType(array.ElementType, ref isInternal);

            // Unresolved types are reported by the compiler itself; do not add a second error.
            case INamedTypeSymbol { TypeKind: TypeKind.Error }:
                return null;

            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    switch (current.DeclaredAccessibility)
                    {
                        case Accessibility.Public:
                            break;
                        // GeneratedMappers lives in the same assembly, so internal and
                        // protected internal types are reachable from internal members.
                        case Accessibility.Internal:
                        case Accessibility.ProtectedOrInternal:
                            isInternal = true;
                            break;
                        default:
                            return current;
                    }
                }

                foreach (var typeArgument in named.TypeArguments)
                {
                    var inaccessible = FindInaccessibleType(typeArgument, ref isInternal);
                    if (inaccessible is not null)
                    {
                        return inaccessible;
                    }
                }
                return null;

            default:
                return null;
        }
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
        if (type is IArrayTypeSymbol)
        {
            return "Array";
        }

        if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            string def = namedType.OriginalDefinition.ToDisplayString();
            if (def == "System.Collections.Generic.List<T>"
                || def == "System.Collections.Generic.IList<T>"
                || def == "System.Collections.Generic.ICollection<T>"
                || def == "System.Collections.Generic.IReadOnlyList<T>"
                || def == "System.Collections.Generic.IReadOnlyCollection<T>")
            {
                return "List";
            }
        }

        return "Enumerable";
    }

    /// <summary>
    /// How the generated code reads a source collection of type <paramref name="type"/>:
    /// <c>"Array"</c> (one-dimensional array: <c>Length</c> + <c>for</c>), <c>"List"</c> (<c>List&lt;T&gt;</c>:
    /// <c>Count</c> + <c>for</c>), <c>"IndexedList"</c> (<c>IList&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>:
    /// <c>Count</c> + <c>for</c> over the indexer, no enumerator), <c>"Counted"</c> (<c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, or a class/struct with a public <c>int Count</c>: <c>Count</c> +
    /// <c>foreach</c> over the declared type), otherwise <c>"Enumerable"</c> (<c>foreach</c>, size unknown).
    /// </summary>
    public static string GetCollectionSourceKind(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.IsSZArray ? "Array" : "Enumerable";
        }

        if (type is not INamedTypeSymbol named)
        {
            return "Enumerable";
        }

        if (named.IsGenericType)
        {
            switch (named.OriginalDefinition.ToDisplayString())
            {
                case "System.Collections.Generic.List<T>":
                    return "List";
                case "System.Collections.Generic.IList<T>":
                case "System.Collections.Generic.IReadOnlyList<T>":
                    return "IndexedList";
                case "System.Collections.Generic.ICollection<T>":
                case "System.Collections.Generic.IReadOnlyCollection<T>":
                    return "Counted";
            }
        }

        // Other interfaces may inherit Count from both ICollection<T> and IReadOnlyCollection<T> (ambiguous).
        if (named.TypeKind == TypeKind.Interface)
        {
            return "Enumerable";
        }

        for (var current = named; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("Count"))
            {
                if (member is IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } count
                    && count.GetMethod is { DeclaredAccessibility: Accessibility.Public }
                    && count.Type.SpecialType == SpecialType.System_Int32)
                {
                    return "Counted";
                }
            }
        }

        return "Enumerable";
    }

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// When both <paramref name="sourcePropType"/> and <paramref name="destPropType"/> are collections whose element
    /// types differ, describes the element mapping; otherwise <see langword="null"/> (same element types, also when only
    /// their nullable annotations differ: the collection is assigned as is).
    /// </summary>
    public static CollectionMapping? DetectCollectionMapping(ITypeSymbol sourcePropType, ITypeSymbol destPropType)
    {
        if (!TryGetCollectionElementType(sourcePropType, out var srcElem) || srcElem is null)
        {
            return null;
        }

        if (!TryGetCollectionElementType(destPropType, out var dstElem) || dstElem is null)
        {
            return null;
        }

        // Element types that differ only by nullable annotations (string? and string) are the same type: the collection is
        // assigned as is (IM0023 when the elements narrow), not mapped element by element.
        if (srcElem.ToDisplayString() == dstElem.ToDisplayString()
            || GetFullyQualifiedName(srcElem) == GetFullyQualifiedName(dstElem))
        {
            return null;
        }

        // The helper parameter is declared nullable: drop a top-level `?` of the property type first.
        string sourceType = sourcePropType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(FullyQualifiedNullableFormat);

        return new CollectionMapping(
            elementMethod: $"MapTo{dstElem.Name}",
            outputType: GetCollectionOutputType(destPropType),
            pairKey: $"{GetFullyQualifiedName(srcElem)}->{GetFullyQualifiedName(dstElem)}",
            sourceKind: GetCollectionSourceKind(sourcePropType),
            sourceTypeFqn: sourceType,
            destElementFqn: dstElem.ToDisplayString(FullyQualifiedNullableFormat));
    }
}

/// <summary>Result of <see cref="SymbolHelpers.DetectCollectionMapping"/>; copied into <see cref="PropertyMappingDescriptor"/>.</summary>
internal sealed class CollectionMapping
{
    public CollectionMapping(string elementMethod, string outputType, string pairKey, string sourceKind,
        string sourceTypeFqn, string destElementFqn)
    {
        ElementMethod = elementMethod;
        OutputType = outputType;
        PairKey = pairKey;
        SourceKind = sourceKind;
        SourceTypeFqn = sourceTypeFqn;
        DestElementFqn = destElementFqn;
    }

    /// <summary><c>MapTo{DestElement}</c>; the plan may qualify it (IM0013), see <see cref="PairKey"/>.</summary>
    public string ElementMethod { get; }

    /// <summary>See <see cref="SymbolHelpers.GetCollectionOutputType"/>.</summary>
    public string OutputType { get; }

    /// <summary>Type pair of the element mapping.</summary>
    public string PairKey { get; }

    /// <summary>See <see cref="SymbolHelpers.GetCollectionSourceKind"/>.</summary>
    public string SourceKind { get; }

    /// <summary>Fully qualified source property type, without a top-level <c>?</c>.</summary>
    public string SourceTypeFqn { get; }

    /// <summary>Fully qualified destination element type (nullable annotations kept).</summary>
    public string DestElementFqn { get; }
}
