using System.Collections.Immutable;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// IM0002: a destination property that the generated assignment cannot fill. Without the check the generated file failed
/// to compile (<c>CS0029</c>/<c>CS0266</c> for incompatible types, <c>CS1061</c> for a collection element without a
/// mapping). A property with IM0002 is not generated; the rest of the mapping is.
/// </summary>
internal static class PropertyTypeCheck
{
    /// <summary>
    /// Diagnostic property of an IM0002 for a collection element without a mapping: the element type pairs (the source
    /// element type and its base types and interfaces → the destination element type, <c>|</c>-separated). The diagnostic
    /// is reported, and the property skipped, only when the plan generates none of them
    /// (<see cref="MappingPlan.GeneratesAny"/>).
    /// </summary>
    internal const string MissingElementPairsKey = "IfNoMappingFor";

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
        string sourceDisplay, IPropertySymbol destProp, INamedTypeSymbol destSymbol, Origin origin, LocationInfo? location)
    {
        if (IsAssignable(compilation, sourceType, destType)) return null;

        var st = Display(sourceType);
        var dt = Display(destType);
        string reason;
        if (IsObject(sourceType) && IsObject(destType))
        {
            reason = $"'{sourceDisplay}' is '{st}' and the destination is '{dt}': nested objects are not mapped automatically yet. "
                     + origin switch
                     {
                         Origin.Attribute => $"Map it with [MapConverter(typeof(...))] (an ITypeConverter<{st}, {dt}>) on the source property, "
                                             + $"or in a profile with CreateMap<{st}, {dt}>() and ForMember(d => d.{destProp.Name}, o => o.MapFrom(s => s.{SourceMember(sourceDisplay)}.MapTo{destType.Name}())); "
                                             + "or mark the destination property [Ignore].",
                         Origin.Profile => $"Configure it with ForMember(d => d.{destProp.Name}, o => o.MapFrom(s => s.{SourceMember(sourceDisplay)}.MapTo{destType.Name}())) "
                                           + $"and a mapping '{st}' → '{dt}', with o.UseConverter<T>() (an ITypeConverter<{st}, {dt}>), or with o.Ignore().",
                         _ => ReverseHint(destProp),
                     };
        }
        else
        {
            reason = $"'{sourceDisplay}' is '{st}', which cannot be assigned to '{dt}'. "
                     + origin switch
                     {
                         Origin.Attribute => $"Add [MapConverter(typeof(...))] (an ITypeConverter<{st}, {dt}>) to the source property, "
                                             + "map it in a profile with ForMember(..., o => o.MapFrom(...)), or mark the destination property [Ignore].",
                         Origin.Profile => $"Configure it with ForMember(d => d.{destProp.Name}, o => o.MapFrom(...)) that converts the value, "
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
    public static DiagnosticInfo? CheckMapFrom(Compilation compilation, ITypeSymbol? expressionType, IPropertySymbol destProp,
        INamedTypeSymbol destSymbol, LocationInfo? location)
    {
        if (expressionType is null || expressionType.TypeKind == TypeKind.Error
            || IsAssignable(compilation, expressionType, destProp.Type)) return null;

        var et = Display(expressionType);
        var dt = Display(destProp.Type);
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
        ITypeSymbol destElement, string sourceDisplay, IPropertySymbol destProp, INamedTypeSymbol destSymbol,
        LocationInfo? location, out bool mappedElsewhere)
    {
        mappedElsewhere = IsMappedByExistingMethod(compilation, collection.ElementMethod, sourceElement, destElement);
        if (mappedElsewhere) return null;

        var se = Display(sourceElement);
        var de = Display(destElement);
        return Create(destProp, destSymbol,
                $"the elements of '{sourceDisplay}' are '{se}' and the elements of the destination are '{de}', and there is no "
                + $"mapping from '{se}' to '{de}'. Add [MapTo(typeof({de}))] to '{se}', [MapFrom(typeof({se}))] to '{de}', "
                + $"or CreateMap<{se}, {de}>() in a profile.", location)
            .WithProperty(MissingElementPairsKey, string.Join("|", ElementPairKeys(sourceElement, destElement)));
    }

    /// <summary>
    /// The pair keys (<c>{source}-&gt;{dest}</c>, as <see cref="MappingDescriptor.PairKey"/>) under which a mapping of this
    /// compilation maps an element: the source element type, its base types and its interfaces (the generated call
    /// <c>item.MapToX()</c> binds to the mapping of a base type too).
    /// </summary>
    public static ImmutableArray<string> ElementPairKeys(ITypeSymbol sourceElement, ITypeSymbol destElement)
    {
        var dest = SymbolHelpers.GetFullyQualifiedName(destElement);
        var builder = ImmutableArray.CreateBuilder<string>();
        for (var type = sourceElement; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
            builder.Add($"{SymbolHelpers.GetFullyQualifiedName(type)}->{dest}");
        foreach (var iface in sourceElement.AllInterfaces)
            builder.Add($"{SymbolHelpers.GetFullyQualifiedName(iface)}->{dest}");
        return builder.ToImmutable();
    }

    private static DiagnosticInfo Create(IPropertySymbol destProp, INamedTypeSymbol destSymbol, string reason, LocationInfo? location)
        => new DiagnosticInfo(DiagnosticDescriptors.IncompatiblePropertyTypes, destProp.Name, destSymbol.ToDisplayString(), reason)
            .WithLocation(location)
            .WithProperty(MappingAnalyzer.PropertyNameKey, destProp.Name);

    private static string ReverseHint(IPropertySymbol destProp)
        => $"The reverse mapping of ReverseMap() maps by name only: declare it with CreateMap and configure '{destProp.Name}' "
           + "there (ForMember with MapFrom, UseConverter or Ignore), or remove ReverseMap().";

    // `source.Address` → `Address` for the hint.
    private static string SourceMember(string sourceDisplay)
    {
        var dot = sourceDisplay.LastIndexOf('.');
        return dot < 0 ? sourceDisplay : sourceDisplay.Substring(dot + 1);
    }

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
    /// </summary>
    private static bool IsObject(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return false;
        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return false;
        if (named.SpecialType != SpecialType.None) return false;
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;
        if (SymbolHelpers.TryGetCollectionElementType(named, out _)) return false;
        var ns = named.ContainingNamespace?.ToDisplayString();
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
            if (ns is null) continue;
            foreach (var type in ns.GetTypeMembers())
            {
                if (!type.MightContainExtensionMethods) continue;
                foreach (var member in type.GetMembers(method))
                {
                    if (member is IMethodSymbol { IsExtensionMethod: true, Parameters.Length: 1, IsGenericMethod: false } candidate
                        && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly)
                        && IsReceiverConversion(compilation, sourceElement, candidate.Parameters[0].Type)
                        && compilation.ClassifyConversion(candidate.ReturnType, destElement).IsImplicit)
                        return true;
                }
            }
        }

        return false;
    }

    // The conversions C# allows for the receiver of an extension method call: identity, implicit reference, boxing.
    private static bool IsReceiverConversion(Compilation compilation, ITypeSymbol receiver, ITypeSymbol parameter)
    {
        if (compilation is not CSharpCompilation csharp) return SymbolEqualityComparer.Default.Equals(receiver, parameter);
        var conversion = csharp.ClassifyConversion(receiver, parameter);
        return conversion.IsIdentity || (conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing));
    }

    private static string Display(ITypeSymbol type) => type.ToDisplayString();
}
