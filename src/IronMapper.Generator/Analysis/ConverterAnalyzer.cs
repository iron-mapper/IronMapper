using System.Collections.Generic;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.Generator.Analysis;

/// <summary>A converter as the generated code uses it.</summary>
internal readonly struct ConverterUse
{
    public ConverterUse(string fullName, bool isCached)
    {
        FullName = fullName;
        IsCached = isCached;
    }

    /// <summary>Fully qualified name (<c>global::App.PriceConverter</c>).</summary>
    public string FullName { get; }

    /// <summary>
    /// The generated code reuses one instance (<c>ConverterCache&lt;T&gt;.Instance</c>) instead of <c>new T()</c> per call.
    /// </summary>
    public bool IsCached { get; }
}

/// <summary>
/// Checks the converters of one mapping (<c>[MapConverter]</c>, <c>UseConverter&lt;T&gt;()</c>,
/// <c>ConvertUsing&lt;T&gt;()</c>): whether the generated code can create them (IM0015 otherwise) and whether one instance
/// can be reused for every call.
/// </summary>
/// <remarks>
/// A converter is cached only when creating it is observably the same as reusing it: a class (not a struct) with a public
/// parameterless constructor, no instance fields anywhere in its hierarchy except <see cref="object"/> (auto-property
/// backing fields and field-like events included), no finalizer, and constructors without code (implicit, or an empty
/// body whose <c>base()</c>/<c>this()</c> call has no arguments; a primary constructor without parameters). Every type of
/// the hierarchy must be declared in source: for a type from a referenced assembly the generator sees neither its private
/// fields nor its constructor bodies, so such converters are created per call, as before.
/// </remarks>
internal sealed class ConverterAnalyzer
{
    private readonly Compilation _compilation;
    private readonly string _sourceDisplay;
    private readonly string _destDisplay;
    private List<DiagnosticInfo>? _diagnostics;

    public ConverterAnalyzer(Compilation compilation, ITypeSymbol source, ITypeSymbol dest)
    {
        _compilation = compilation;
        _sourceDisplay = source.ToDisplayString();
        _destDisplay = dest.ToDisplayString();
    }

    /// <summary>IM0015 for each converter of the mapping that generated code cannot create; empty when none.</summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics => (IReadOnlyList<DiagnosticInfo>?)_diagnostics ?? System.Array.Empty<DiagnosticInfo>();

    /// <summary>
    /// A converter type did not resolve. The compiler reports it at the user's code; the mapping is skipped without a
    /// diagnostic of its own, so the generated code does not repeat the error.
    /// </summary>
    public bool HasUnresolvedType { get; private set; }

    /// <summary>The mapping can be generated: every converter resolved and can be created.</summary>
    public bool CanGenerate => !HasUnresolvedType && _diagnostics is null;

    /// <summary>
    /// Checks <paramref name="converter"/>, used as described by <paramref name="usage"/> (e.g. <c>[MapConverter] on
    /// 'App.Order.Price'</c>); IM0015 points to <paramref name="location"/> (the converter type in the user's code). Returns <see langword="null"/> when the mapping cannot use it (see
    /// <see cref="HasUnresolvedType"/> and <see cref="Diagnostics"/>).
    /// </summary>
    public ConverterUse? Add(ITypeSymbol converter, string usage, LocationInfo? location, CancellationToken ct)
    {
        // The type arguments of typeof(Converter<>) are error types: that case is IM0015, not an unresolved type.
        if (converter is not INamedTypeSymbol { IsUnboundGenericType: true } && SymbolHelpers.ContainsErrorType(converter))
        {
            HasUnresolvedType = true;
            return null;
        }

        string? problem = FindCreationProblem(converter);
        if (problem is not null)
        {
            string name = converter.ToDisplayString();
            (_diagnostics ??= new List<DiagnosticInfo>()).Add(new DiagnosticInfo(
                DiagnosticDescriptors.ConverterCannotBeCreated, _sourceDisplay, _destDisplay, name, usage, problem)
                .WithLocation(location));
            return null;
        }

        return new ConverterUse(SymbolHelpers.GetFullyQualifiedName(converter), IsCacheable(converter, ct));
    }

    /// <summary>Why <c>new T()</c> in the generated class would not compile, or <see langword="null"/> when it does.</summary>
    private string? FindCreationProblem(ITypeSymbol converter)
    {
        if (converter is not INamedTypeSymbol named)
        {
            return "it is not a class or struct";
        }

        if (named.IsUnboundGenericType)
        {
            return "it is an open generic type";
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            return "it is an interface";
        }

        if (named.IsStatic)
        {
            return "it is static";
        }

        if (named.IsAbstract)
        {
            return "it is abstract";
        }

        if (!_compilation.IsSymbolAccessibleWithin(named, _compilation.Assembly))
        {
            return $"it is not accessible from the generated class in namespace IronMapper.Generated ({SymbolHelpers.ToKeyword(named.DeclaredAccessibility)})";
        }

        // A struct always has a parameterless constructor.
        if (named.IsValueType)
        {
            return null;
        }

        IMethodSymbol? inaccessible = null;
        foreach (var ctor in named.InstanceConstructors)
        {
            if (!CanBeCalledWithoutArguments(ctor))
            {
                continue;
            }

            if (_compilation.IsSymbolAccessibleWithin(ctor, _compilation.Assembly))
            {
                return null;
            }

            inaccessible ??= ctor;
        }

        return inaccessible is null
            ? "it has no parameterless constructor"
            : $"its parameterless constructor is {SymbolHelpers.ToKeyword(inaccessible.DeclaredAccessibility)}";
    }

    private static bool CanBeCalledWithoutArguments(IMethodSymbol ctor)
    {
        foreach (var parameter in ctor.Parameters)
        {
            if (!parameter.IsOptional && !parameter.IsParams)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>ConverterCache&lt;T&gt;</c> requires <c>class, new()</c>; the instance is reused only when the converter has no
    /// state and its constructors run no code (see the class remarks).
    /// </summary>
    private static bool IsCacheable(ITypeSymbol converter, CancellationToken ct)
    {
        if (converter is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } named)
        {
            return false;
        }

        bool hasPublicParameterlessCtor = false;
        foreach (var ctor in named.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)
            {
                hasPublicParameterlessCtor = true;
            }
        }

        if (!hasPublicParameterlessCtor)
        {
            return false;
        }

        for (var type = named; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            ct.ThrowIfCancellationRequested();

            // Declared in a referenced assembly: private fields and constructor bodies are not visible.
            if (type.DeclaringSyntaxReferences.IsEmpty)
            {
                return false;
            }

            foreach (var member in type.GetMembers())
            {
                switch (member)
                {
                    // Instance fields, including auto-property backing fields (consts are static).
                    case IFieldSymbol { IsStatic: false }:
                    // A field-like event has a hidden field.
                    case IEventSymbol { IsStatic: false }:
                    case IMethodSymbol { MethodKind: MethodKind.Destructor }:
                        return false;
                    case IMethodSymbol { MethodKind: MethodKind.Constructor } ctor when !ctor.IsImplicitlyDeclared:
                        if (!HasNoCode(ctor, ct))
                        {
                            return false;
                        }

                        break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// An explicit constructor runs no code of its own: an empty block body and a <c>base()</c>/<c>this()</c> call
    /// without arguments, or a primary constructor without parameters whose base call has no arguments.
    /// </summary>
    private static bool HasNoCode(IMethodSymbol ctor, CancellationToken ct)
    {
        if (ctor.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        foreach (var reference in ctor.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(ct))
            {
                case ConstructorDeclarationSyntax declaration:
                    if (declaration.ExpressionBody is not null
                        || declaration.Body is not { Statements.Count: 0 }
                        || declaration.Initializer is { ArgumentList.Arguments.Count: > 0 })
                    {
                        return false;
                    }

                    break;

                // Primary constructor: parameters may be captured into hidden fields.
                case TypeDeclarationSyntax type:
                    if (type.ParameterList is { Parameters.Count: > 0 })
                    {
                        return false;
                    }

                    if (type.BaseList is not null)
                    {
                        foreach (var baseType in type.BaseList.Types)
                        {
                            if (baseType is PrimaryConstructorBaseTypeSyntax { ArgumentList.Arguments.Count: > 0 })
                            {
                                return false;
                            }
                        }
                    }
                    break;

                default:
                    return false;
            }
        }

        return true;
    }
}
