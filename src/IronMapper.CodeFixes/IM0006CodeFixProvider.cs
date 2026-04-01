using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.CodeFixes;

/// <summary>
/// Code fix for IM0006 — <c>[MapProperty]</c> destination name does not exist on the destination type.
/// Suggests the closest existing destination property name (Levenshtein distance ≤ 2).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IM0006CodeFixProvider))]
[Shared]
public sealed class IM0006CodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "IM0006";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticId);

    /// <inheritdoc/>
    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel is null) return;

        var diagnostic = context.Diagnostics.First();
        var (wrongName, destTypeName) = ExtractNamesFromMessage(diagnostic);
        if (wrongName is null || destTypeName is null) return;

        // Find the destination type symbol.
        var destTypeSymbol = semanticModel.Compilation.GetSymbolsWithName(
            n => n == destTypeName, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        if (destTypeSymbol is null) return;

        // Collect public, settable property names.
        var candidateNames = destTypeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public
                     && !p.IsStatic
                     && p.SetMethod is not null)
            .Select(p => p.Name)
            .ToList();

        // Find the best match within Levenshtein distance ≤ 2.
        var suggestions = candidateNames
            .Select(n => (name: n, dist: LevenshteinDistance(wrongName, n)))
            .Where(t => t.dist <= 2)
            .OrderBy(t => t.dist)
            .Select(t => t.name)
            .ToList();

        if (!suggestions.Any()) return;

        // Find the MapProperty attribute node with the wrong value.
        var attributeNode = FindMapPropertyAttributeWithValue(root, wrongName);
        if (attributeNode is null) return;

        foreach (var suggestion in suggestions)
        {
            var localSuggestion = suggestion;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Did you mean '{localSuggestion}'?",
                    createChangedDocument: ct =>
                        ReplaceMapPropertyValueAsync(
                            context.Document, attributeNode, wrongName, localSuggestion, ct),
                    equivalenceKey: DiagnosticId + "_Suggest_" + localSuggestion),
                diagnostic);
        }
    }

    private static async Task<Document> ReplaceMapPropertyValueAsync(
        Document document,
        AttributeSyntax attributeNode,
        string oldValue,
        string newValue,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // Replace the string literal argument inside [MapProperty("OldName")] → [MapProperty("NewName")].
        var oldLiteral = attributeNode.ArgumentList?.Arguments
            .Select(a => a.Expression)
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.Token.ValueText == oldValue);

        if (oldLiteral is null) return document;

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(newValue))
            .WithTriviaFrom(oldLiteral);

        var newRoot = root.ReplaceNode(oldLiteral, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }

    private static AttributeSyntax? FindMapPropertyAttributeWithValue(SyntaxNode root, string value)
        => root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .FirstOrDefault(a =>
                (a.Name.ToString() == "MapProperty" || a.Name.ToString() == "MapPropertyAttribute")
                && a.ArgumentList?.Arguments.Count > 0
                && a.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                && lit.Token.ValueText == value);

    private static (string? wrongName, string? destTypeName) ExtractNamesFromMessage(Diagnostic diagnostic)
    {
        // Message format: "[MapProperty] destination '{0}' does not exist in type '{1}'"
        var msg = diagnostic.GetMessage();
        var first = ExtractQuotedString(msg, 0);
        var second = first is null ? null : ExtractQuotedString(msg, msg.IndexOf('\'') + first.Length + 2);
        return (first, second);
    }

    private static string? ExtractQuotedString(string text, int startSearch)
    {
        var start = text.IndexOf('\'', startSearch);
        if (start < 0) return null;
        var end = text.IndexOf('\'', start + 1);
        if (end <= start) return null;
        return text.Substring(start + 1, end - start - 1);
    }

    /// <summary>Computes the Levenshtein edit distance between two strings.</summary>
    private static int LevenshteinDistance(string s, string t)
    {
        if (s.Length == 0) return t.Length;
        if (t.Length == 0) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        for (var j = 1; j <= t.Length; j++)
        {
            var cost = string.Compare(s, i - 1, t, j - 1, 1,
                StringComparison.OrdinalIgnoreCase) == 0 ? 0 : 1;
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }

        return d[s.Length, t.Length];
    }
}
