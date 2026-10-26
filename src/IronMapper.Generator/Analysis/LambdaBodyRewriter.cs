using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Rewrites the body of a lambda written in a <c>MappingProfile</c> constructor so that it can be
/// emitted into <c>IronMapper.Generated.GeneratedMappers_{AssemblyName}</c>, a class with no <c>using</c> directives
/// and no profile instance:
/// <list type="bullet">
///   <item>lambda parameters are renamed by symbol (string literals and member names are untouched);</item>
///   <item>namespaces, types and static members get fully qualified names;</item>
///   <item>extension method calls become static calls on their declaring class;</item>
///   <item>members the generated class cannot access (IM0011) and captured profile state or
///   locals (IM0012) are reported instead of producing C# errors in the generated file.</item>
/// </list>
/// </summary>
internal static class LambdaBodyRewriter
{
    /// <summary>
    /// Rewrites a lambda whose body is emitted as an expression (or as a block for transformers).
    /// Returns <see langword="null"/> when <paramref name="expr"/> is not a lambda, or when problems were
    /// found (they are added to <paramref name="diagnostics"/>).
    /// </summary>
    public static string? RewriteBody(
        ExpressionSyntax expr,
        SemanticModel model,
        string[] parameterNames,
        string profileName,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var rewritten = Rewrite(expr, model, parameterNames, profileName, diagnostics, ct);
        return rewritten?.ToString();
    }

    /// <summary>
    /// Rewrites a hook lambda such as <c>(src, dest) =&gt; ...</c> into statements: an expression body
    /// becomes one statement, a block body becomes its inner statements (one per line).
    /// Returns <see langword="null"/> when <paramref name="expr"/> is not such a lambda or problems were found.
    /// </summary>
    public static string? RewriteAsStatements(
        ExpressionSyntax expr,
        SemanticModel model,
        string[] parameterNames,
        string profileName,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        if (expr is not ParenthesizedLambdaExpressionSyntax lambda
            || lambda.ParameterList.Parameters.Count < parameterNames.Length)
        {
            return null;
        }

        var rewritten = Rewrite(expr, model, parameterNames, profileName, diagnostics, ct);
        if (rewritten is null)
        {
            return null;
        }

        if (rewritten is BlockSyntax block)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var statement in block.Statements)
            {
                sb.AppendLine(statement.ToString().TrimStart());
            }

            return sb.ToString().TrimEnd();
        }

        return rewritten + ";";
    }

    private static SyntaxNode? Rewrite(
        ExpressionSyntax expr,
        SemanticModel model,
        string[] parameterNames,
        string profileName,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        if (expr is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        var parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters.ToArray(),
            _ => System.Array.Empty<ParameterSyntax>(),
        };

        var renames = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        for (int i = 0; i < parameters.Length && i < parameterNames.Length; i++)
        {
            if (model.GetDeclaredSymbol(parameters[i], ct) is IParameterSymbol parameter)
            {
                renames[parameter] = parameterNames[i];
            }
        }

        var rewriter = new Rewriter(model, lambda, renames, profileName, ct);
        var body = rewriter.Visit(lambda.Body);
        if (rewriter.Problems.Count == 0)
        {
            return body;
        }

        diagnostics.AddRange(rewriter.Problems);
        return null;
    }

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly LambdaExpressionSyntax _lambda;
        private readonly Dictionary<ISymbol, string> _renames;
        private readonly string _profileName;
        private readonly CancellationToken _ct;
        private readonly HashSet<DiagnosticInfo> _seen = new();

        public List<DiagnosticInfo> Problems { get; } = new();

        public Rewriter(
            SemanticModel model,
            LambdaExpressionSyntax lambda,
            Dictionary<ISymbol, string> renames,
            string profileName,
            CancellationToken ct)
        {
            _model = model;
            _lambda = lambda;
            _renames = renames;
            _profileName = profileName;
            _ct = ct;
        }

        // ------------------------------------------------------------------
        // Simple names
        // ------------------------------------------------------------------

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.IsVar || !IsStandaloneName(node))
            {
                return node;
            }

            var symbol = GetSymbol(node);
            return symbol is null ? node : RewriteSimpleName(node, node.Identifier.Text, symbol) ?? node;
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            // Visit type arguments first; the name itself is rewritten only when it stands alone.
            var visited = (GenericNameSyntax)base.VisitGenericName(node)!;
            if (!IsStandaloneName(node))
            {
                return visited;
            }

            var symbol = GetSymbol(node);
            if (symbol is INamedTypeSymbol type)
            {
                ReportIfInaccessible(type, type, node);
                return SyntaxFactory.ParseTypeName(SymbolHelpers.GetFullyQualifiedName(type)).WithTriviaFrom(node);
            }

            return symbol is null ? visited : RewriteSimpleName(node, visited.ToString(), symbol) ?? visited;
        }

        /// <summary>
        /// Handles a name that is not the right-hand part of a member access: parameters, locals,
        /// namespaces, types and members of the enclosing (profile) type.
        /// </summary>
        private SyntaxNode? RewriteSimpleName(SimpleNameSyntax node, string text, ISymbol symbol)
        {
            switch (symbol)
            {
                case IParameterSymbol when _renames.TryGetValue(symbol, out string? newName):
                    return SyntaxFactory.IdentifierName(newName).WithTriviaFrom(node);

                case IParameterSymbol or ILocalSymbol or IRangeVariableSymbol:
                    if (!IsDeclaredInsideLambda(symbol))
                    {
                        Report(DiagnosticDescriptors.LambdaCapturesState, node,
                            symbol is IParameterSymbol ? "parameter" : "local variable", symbol.Name);
                    }

                    return null;

                case IMethodSymbol { MethodKind: MethodKind.LocalFunction }:
                    if (!IsDeclaredInsideLambda(symbol))
                    {
                        Report(DiagnosticDescriptors.LambdaCapturesState, node, "local function", symbol.Name);
                    }

                    return null;

                case INamespaceSymbol ns:
                    return SyntaxFactory.ParseName(ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).WithTriviaFrom(node);

                case INamedTypeSymbol type:
                    ReportIfInaccessible(type, type, node);
                    return SyntaxFactory.ParseTypeName(SymbolHelpers.GetFullyQualifiedName(type)).WithTriviaFrom(node);

                case IMethodSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol:
                    if (!symbol.IsStatic)
                    {
                        Report(DiagnosticDescriptors.LambdaCapturesState, node, "instance member", symbol.ToDisplayString());
                        return null;
                    }
                    ReportIfInaccessible(symbol, symbol, node);
                    string containing = SymbolHelpers.GetFullyQualifiedName(symbol.ContainingType);
                    return SyntaxFactory.ParseExpression(containing + "." + text).WithTriviaFrom(node);

                default:
                    return null;
            }
        }

        // ------------------------------------------------------------------
        // Member access, invocations, this/base
        // ------------------------------------------------------------------

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            // The right-hand name is kept as written, but it must still be reachable from generated code.
            if (GetSymbol(node) is { } member
                and (IMethodSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol or INamedTypeSymbol))
            {
                ReportIfInaccessible(member, member, node.Name);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // nameof(...) is a compile-time constant; emit it as a literal.
            if (node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
                && _model.GetConstantValue(node, _ct) is { HasValue: true, Value: string name })
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)).WithTriviaFrom(node);
            }

            // Inside `a?.b.Ext()` the receiver `.b` only exists within the conditional access,
            // so such calls cannot become static calls; they are emitted as written.
            if (_model.GetSymbolInfo(node, _ct).Symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension } method
                && node.Expression is MemberAccessExpressionSyntax memberAccess
                && !StartsWithConditionalBinding(memberAccess.Expression))
            {
                // receiver.Ext(args) → global::Ns.ExtClass.Ext(receiver, args)
                ReportIfInaccessible(method, method, memberAccess.Name);
                var receiver = (ExpressionSyntax)Visit(memberAccess.Expression)!;
                var methodName = (SimpleNameSyntax)Visit(memberAccess.Name)!;
                var arguments = (ArgumentListSyntax)Visit(node.ArgumentList)!;

                var allArguments = new[] { receiver.ToString() }
                    .Concat(arguments.Arguments.Select(a => a.ToString()));
                string extensionClass = SymbolHelpers.GetFullyQualifiedName(method.ContainingType);
                return SyntaxFactory.ParseExpression(
                        $"{extensionClass}.{methodName}({string.Join(", ", allArguments)})")
                    .WithTriviaFrom(node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            Report(DiagnosticDescriptors.LambdaCapturesState, node, "the profile instance", "this");
            return node;
        }

        public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node)
        {
            Report(DiagnosticDescriptors.LambdaCapturesState, node, "the profile instance", "base");
            return node;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private ISymbol? GetSymbol(SyntaxNode node)
        {
            var info = _model.GetSymbolInfo(node, _ct);
            if (info.Symbol is not null)
            {
                return info.Symbol;
            }
            // Method groups that are not yet invoked resolve to candidates only.
            return info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null;
        }

        /// <summary>
        /// False for names whose meaning is fixed by their position: the right-hand side of
        /// <c>a.b</c> / <c>a?.b</c> / <c>A.B</c>, object-initializer targets and named arguments.
        /// </summary>
        private static bool IsStandaloneName(SimpleNameSyntax node) => node.Parent switch
        {
            MemberAccessExpressionSyntax ma when ma.Name == node => false,
            MemberBindingExpressionSyntax => false,
            QualifiedNameSyntax q when q.Right == node => false,
            AliasQualifiedNameSyntax => false,
            NameColonSyntax or NameEqualsSyntax => false,
            AssignmentExpressionSyntax a when a.Left == node && a.Parent is InitializerExpressionSyntax => false,
            _ => true,
        };

        private static bool StartsWithConditionalBinding(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case MemberBindingExpressionSyntax or ElementBindingExpressionSyntax:
                        return true;
                    case MemberAccessExpressionSyntax ma:
                        expression = ma.Expression;
                        break;
                    case InvocationExpressionSyntax inv:
                        expression = inv.Expression;
                        break;
                    case ElementAccessExpressionSyntax ea:
                        expression = ea.Expression;
                        break;
                    default:
                        return false;
                }
            }
        }

        private bool IsDeclaredInsideLambda(ISymbol symbol)
            => symbol.DeclaringSyntaxReferences.Any(r =>
                r.SyntaxTree == _lambda.SyntaxTree && _lambda.Span.Contains(r.Span));

        private void ReportIfInaccessible(ISymbol referenced, ISymbol start, SyntaxNode at)
        {
            for (var current = start; current is not null && current is not INamespaceSymbol; current = current.ContainingSymbol)
            {
                switch (current.DeclaredAccessibility)
                {
                    case Accessibility.Private:
                    case Accessibility.Protected:
                    case Accessibility.ProtectedAndInternal:
                        Report(DiagnosticDescriptors.LambdaMemberNotAccessible, at,
                            referenced.ToDisplayString(),
                            current.ToDisplayString(),
                            SymbolHelpers.ToKeyword(current.DeclaredAccessibility));
                        return;
                }
            }
        }

        /// <summary>Reports a problem once (at its first use), pointing to <paramref name="at"/> in the lambda.</summary>
        private void Report(DiagnosticDescriptor descriptor, SyntaxNode at, params string[] args)
        {
            var info = new DiagnosticInfo(descriptor, new[] { _profileName }.Concat(args).ToArray());
            if (_seen.Add(info))
            {
                Problems.Add(info.WithLocation(LocationInfo.From(at)));
            }
        }
    }
}
