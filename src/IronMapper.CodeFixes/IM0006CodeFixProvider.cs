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
/// Suggests the closest destination property names (Levenshtein distance ≤ 2), taken from the diagnostic
/// (property <c>DestinationProperties</c>); the diagnostic points to the attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IM0006CodeFixProvider))]
[Shared]
public sealed class IM0006CodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "IM0006";
    private const string DestinationPropertiesKey = "DestinationProperties";

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
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic points to the [MapProperty] attribute and carries the destination property names.
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree != root.SyntaxTree)
            {
                continue;
            }

            if (!diagnostic.Properties.TryGetValue(DestinationPropertiesKey, out string? names) || string.IsNullOrEmpty(names))
            {
                continue;
            }

            var attributeNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
            string? wrongName = attributeNode?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                ? literal.Token.ValueText
                : null;
            if (attributeNode is null || wrongName is null)
            {
                continue;
            }

            // The closest destination property names, Levenshtein distance <= 2 (case-insensitive).
            var suggestions = names!.Split(';')
                .Select(n => (name: n, dist: LevenshteinDistance(wrongName, n)))
                .Where(t => t.dist <= 2)
                .OrderBy(t => t.dist)
                .ThenBy(t => t.name, StringComparer.Ordinal)
                .Select(t => t.name)
                .ToList();

            foreach (string? suggestion in suggestions)
            {
                string localSuggestion = suggestion;
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
    }

    private static async Task<Document> ReplaceMapPropertyValueAsync(
        Document document,
        AttributeSyntax attributeNode,
        string oldValue,
        string newValue,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Replace the string literal argument inside [MapProperty("OldName")] → [MapProperty("NewName")].
        var oldLiteral = attributeNode.ArgumentList?.Arguments
            .Select(a => a.Expression)
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.Token.ValueText == oldValue);

        if (oldLiteral is null)
        {
            return document;
        }

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(newValue))
            .WithTriviaFrom(oldLiteral);

        var newRoot = root.ReplaceNode(oldLiteral, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>Computes the Levenshtein edit distance between two strings.</summary>
    /// <param name="s">The first string.</param>
    /// <param name="t">The second string.</param>
    /// <returns>The minimum number of single-character edits (insertions, deletions, substitutions) needed to transform <paramref name="s"/> into <paramref name="t"/>.</returns>
    private static int LevenshteinDistance(string s, string t)
    {
        if (s.Length == 0)
        {
            return t.Length;
        }

        if (t.Length == 0)
        {
            return s.Length;
        }

        int[,] d = new int[s.Length + 1, t.Length + 1];
        for (int i = 0; i <= s.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= t.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
        {
                int cost = string.Compare(s, i - 1, t, j - 1, 1,
                StringComparison.OrdinalIgnoreCase) == 0 ? 0 : 1;
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }
        }

        return d[s.Length, t.Length];
    }
}
