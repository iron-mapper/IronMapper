using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IronMapper.Generator.Analysis;

/// <summary>The factory of a profile mapping: what the generated code calls, and the method whose parameters are matched.</summary>
internal sealed class ProfileFactory
{
    public ProfileFactory(FactoryDescriptor descriptor, IMethodSymbol? method)
    {
        Descriptor = descriptor;
        Method = method;
    }

    public FactoryDescriptor Descriptor { get; }

    /// <summary>The static method or the lambda of <c>ConstructWith</c>; <see langword="null"/> for <c>ConstructUsing</c>.</summary>
    public IMethodSymbol? Method { get; }
}

/// <summary>
/// Reads the argument of <c>ConstructWith</c> (a <c>Delegate</c> parameter) in a profile. A method group or a lambda with
/// typed parameters has a natural delegate type (C# 10), and so does <c>new Func&lt;…&gt;(D.Restore)</c>: the operation is a
/// delegate creation whose target is the method, or the lambda. Anything the generated code cannot call is IM0020 at the
/// argument, instead of an error in the generated file.
/// </summary>
internal static class FactoryAnalyzer
{
    /// <summary>
    /// The factory of <c>ConstructWith(<paramref name="argument"/>)</c>, or <see langword="null"/> when it cannot be used:
    /// IM0020, or the IM0011/IM0012 problems of a lambda, are added to <paramref name="problems"/>.
    /// </summary>
    public static ProfileFactory? ConstructWith(ExpressionSyntax argument, SemanticModel model, INamedTypeSymbol source,
        INamedTypeSymbol dest, string profileName, List<DiagnosticInfo> problems, CancellationToken ct)
    {
        var compilation = model.Compilation;
        var location = LocationInfo.From(argument);

        void Invalid(string reason)
            => problems.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFactory, source.ToDisplayString(), dest.ToDisplayString(),
                    reason)
                .WithLocation(location));

        // From the argument down: conversions (to Delegate), the delegate creation, its target.
        var operation = argument.Parent is ArgumentSyntax argumentSyntax && model.GetOperation(argumentSyntax, ct) is IArgumentOperation passed
            ? passed.Value
            : model.GetOperation(argument, ct);
        var target = operation;
        while (target is IConversionOperation or IDelegateCreationOperation)
        {
            target = target is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)target).Target;
        }
        if (target is IMethodReferenceOperation reference)
        {
            var method = reference.Method;
            string display = $"{method.ContainingType.Name}.{method.Name}";
            if (method.MethodKind == MethodKind.LocalFunction)
            {
                Invalid($"'{method.Name}' is a local function, which the generated class cannot call. Declare it as a static method of a type, or pass a lambda.");
                return null;
            }

            if (!method.IsStatic || reference.Instance is not null)
            {
                Invalid($"'{display}' is an instance method; ConstructWith takes a static method, such as a factory method of '{dest.Name}', or a lambda with typed parameters.");
                return null;
            }

            if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
            {
                var hidden = method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                    ? (ISymbol?)FirstInaccessibleType(method.ContainingType, compilation) ?? method
                    : method;
                Invalid($"'{display}' cannot be called by the generated class: '{hidden.ToDisplayString()}' is {SymbolHelpers.ToKeyword(hidden.DeclaredAccessibility)}. Make it public or internal.");
                return null;
            }

            if (Check(method, $"'{display}'", dest, compilation) is { } wrong)
            {
                Invalid(wrong);
                return null;
            }

            string call = $"{SymbolHelpers.GetFullyQualifiedName(method.ContainingType)}.{method.Name}";
            if (method.IsGenericMethod)
            {
                call += $"<{string.Join(", ", method.TypeArguments.Select(SymbolHelpers.GetFullyQualifiedName))}>";
            }

            return new ProfileFactory(new FactoryDescriptor(FactoryKind.Method, call, null, false, $"factory '{display}'"), method);
        }

        if (target is IAnonymousFunctionOperation function)
        {
            if (function.Syntax is not LambdaExpressionSyntax lambda)
            {
                Invalid("ConstructWith takes a lambda, not an anonymous method (delegate { … }): write (string name, int age) => …");
                return null;
            }

            var method = function.Symbol;
            // (name) => …: no natural type (CS8917); the compiler binds the parameters with an error type.
            if (method.Parameters.Any(p => SymbolHelpers.ContainsErrorType(p.Type)))
            {
                Invalid(UntypedLambda(dest));
                return null;
            }

            if (Check(method, "the lambda", dest, compilation) is { } wrong)
            {
                Invalid(wrong);
                return null;
            }

            foreach (var parameter in method.Parameters)
            {
                if (FirstInaccessibleType(parameter.Type, compilation) is { } hidden)
                {
                    Invalid($"the type '{parameter.Type.ToDisplayString()}' of the lambda parameter '{parameter.Name}' cannot be used by the generated class: '{hidden.ToDisplayString()}' is {SymbolHelpers.ToKeyword(hidden.DeclaredAccessibility)}.");
                    return null;
                }
            }

            var rewritten = LambdaBodyRewriter.RewriteFactory(lambda, method, model, profileName, problems, ct);
            if (rewritten is null)
            {
                return null;
            }

            var (parameters, body, isBlock) = rewritten.Value;
            return new ProfileFactory(
                new FactoryDescriptor(FactoryKind.Lambda, body, parameters, isBlock, "the lambda of ConstructWith"), method);
        }

        // The compiler reports the argument too (no natural type, CS8917, or not a delegate); IM0020 says what ConstructWith needs.
        var symbolInfo = model.GetSymbolInfo(argument, ct);
        if (symbolInfo.Symbol is null && symbolInfo.CandidateSymbols.Length > 1 && symbolInfo.CandidateSymbols.All(s => s is IMethodSymbol))
        {
            var first = (IMethodSymbol)symbolInfo.CandidateSymbols[0];
            string name = $"{first.ContainingType.Name}.{first.Name}";
            string typed = string.Join(", ", first.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
            string names = string.Join(", ", first.Parameters.Select(p => p.Name));
            Invalid($"'{name}' has {symbolInfo.CandidateSymbols.Length} overloads, so the delegate type of the method group cannot be inferred. Choose one with a lambda: ConstructWith(({typed}) => {name}({names})).");
            return null;
        }

        if (argument is LambdaExpressionSyntax)
        {
            Invalid(UntypedLambda(dest));
            return null;
        }

        Invalid($"ConstructWith takes a static method group, such as ConstructWith({dest.Name}.Create), or a lambda with typed parameters, written in the call; '{argument}' is neither, so the generator cannot read it.");
        return null;
    }

    /// <summary>
    /// The call of <c>ConvertUsing(<paramref name="argument"/>)</c> written as a method group (<c>ConvertUsing(UserId.From)</c>):
    /// <c>global::App.UserId.From(source)</c>, or <see langword="null"/> with IM0022 in <paramref name="problems"/> when the
    /// generated class cannot call it (an instance method, a local function, a delegate in a variable, a private method).
    /// The compiler has checked the parameter and the return type (<c>Func&lt;TSource, TDest&gt;</c>). A lambda is rewritten by
    /// <see cref="LambdaBodyRewriter"/> instead. <paramref name="callName"/> names the method in messages: <c>ConvertUsing</c>,
    /// or <c>ReverseMap</c> with the converter of the reverse mapping (<paramref name="source"/> and <paramref name="dest"/>
    /// are then those of the reverse mapping).
    /// </summary>
    public static string? ConvertUsing(ExpressionSyntax argument, SemanticModel model, INamedTypeSymbol source,
        INamedTypeSymbol dest, string callName, List<DiagnosticInfo> problems, CancellationToken ct)
    {
        var compilation = model.Compilation;

        void Invalid(string reason)
            => problems.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidConverter, source.ToDisplayString(), dest.ToDisplayString(),
                    reason)
                .WithLocation(LocationInfo.From(argument)));

        var target = argument.Parent is ArgumentSyntax argumentSyntax && model.GetOperation(argumentSyntax, ct) is IArgumentOperation passed
            ? passed.Value
            : model.GetOperation(argument, ct);
        while (target is IConversionOperation or IDelegateCreationOperation)
        {
            target = target is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)target).Target;
        }

        if (target is not IMethodReferenceOperation reference)
        {
            // An error in the argument is the compiler's (it reports it there).
            if (target is null || target.Type is IErrorTypeSymbol || model.GetSymbolInfo(argument, ct).CandidateReason != CandidateReason.None)
            {
                return null;
            }

            Invalid($"{callName} takes a lambda, such as {callName}(s => …), or a static method group, such as {callName}({dest.Name}.From), written in the call; '{argument}' is neither, so the generated method cannot call it.");
            return null;
        }

        var method = reference.Method;
        string display = $"{method.ContainingType.Name}.{method.Name}";
        if (method.MethodKind == MethodKind.LocalFunction)
        {
            Invalid($"'{method.Name}' is a local function, which the generated class cannot call. Declare it as a static method of a type, or pass a lambda.");
            return null;
        }

        if (!method.IsStatic || reference.Instance is not null)
        {
            Invalid($"'{display}' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.");
            return null;
        }

        if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
        {
            var hidden = method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                ? (ISymbol?)FirstInaccessibleType(method.ContainingType, compilation) ?? method
                : method;
            Invalid($"'{display}' cannot be called by the generated class: '{hidden.ToDisplayString()}' is {SymbolHelpers.ToKeyword(hidden.DeclaredAccessibility)}. Make it public or internal.");
            return null;
        }

        string call = $"{SymbolHelpers.GetFullyQualifiedName(method.ContainingType)}.{method.Name}";
        if (method.IsGenericMethod)
        {
            call += $"<{string.Join(", ", method.TypeArguments.Select(SymbolHelpers.GetFullyQualifiedName))}>";
        }

        return $"{call}(source)";
    }

    private static string UntypedLambda(INamedTypeSymbol dest)
        => $"the parameters of the lambda need types, such as ConstructWith((string name, int age) => new {dest.Name}(name, age)): the parameters are matched to the source by name, and the delegate type comes from their types.";

    // The return type and the parameters of a factory: the destination or a type derived from it, not nullable; values only.
    private static string? Check(IMethodSymbol method, string what, INamedTypeSymbol dest, Compilation compilation)
    {
        if (method.ReturnsVoid)
        {
            return $"{what} returns void; it must return '{dest.ToDisplayString()}'.";
        }

        var conversion = compilation.ClassifyConversion(method.ReturnType, dest);
        if (!conversion.IsIdentity && !(conversion.IsImplicit && conversion.IsReference))
        {
            return $"{what} returns '{method.ReturnType.ToDisplayString()}', not '{dest.ToDisplayString()}' or a type derived from it.";
        }

        if (method.ReturnType.IsReferenceType && method.ReturnNullableAnnotation == NullableAnnotation.Annotated)
        {
            return $"{what} returns '{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier))}', which may be null, and the mapping returns '{dest.Name}'. Return '{dest.Name}' (throw instead of returning null).";
        }

        if (method.Parameters.FirstOrDefault(p => !ConstructorSelection.IsPassedByValue(p)) is { } byRef)
        {
            return $"the parameter '{byRef.Name}' of {what} is passed by reference ({byRef.RefKind.ToString().ToLowerInvariant()}); the generated code passes values only.";
        }

        return null;
    }

    // The type itself, a containing type or a type argument that the generated class cannot reference.
    private static INamedTypeSymbol? FirstInaccessibleType(ITypeSymbol type, Compilation compilation)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FirstInaccessibleType(array.ElementType, compilation);
            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (!compilation.IsSymbolAccessibleWithin(current.OriginalDefinition, compilation.Assembly))
                    {
                        return current;
                    }
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (FirstInaccessibleType(argument, compilation) is { } hidden)
                    {
                        return hidden;
                    }
                }

                return null;
            default:
                return null;
        }
    }
}
