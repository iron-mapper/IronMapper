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
/// Code fix for IM0001 — unmapped destination property.
/// Offers to add an <c>[Ignore]</c> attribute to the destination property so the warning is suppressed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IM0001CodeFixProvider))]
[Shared]
public sealed class IM0001CodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "IM0001";

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

        var diagnostic = context.Diagnostics.First();

        // The diagnostic message contains the property name as the first argument.
        // We look for a property declaration whose name matches anywhere in the document.
        var propertyName = ExtractPropertyNameFromMessage(diagnostic);
        if (propertyName is null) return;

        // Find the destination property declaration in the document.
        var propertyDecl = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.Text == propertyName);

        if (propertyDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [Ignore] to '{propertyName}'",
                createChangedDocument: ct =>
                    AddIgnoreAttributeAsync(context.Document, propertyDecl, ct),
                equivalenceKey: DiagnosticId + "_AddIgnore_" + propertyName),
            diagnostic);
    }

    private static async Task<Document> AddIgnoreAttributeAsync(
        Document document,
        PropertyDeclarationSyntax propertyDecl,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var ignoreAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("Ignore"));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(ignoreAttribute))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        var updatedProperty = propertyDecl
            .AddAttributeLists(attributeList);

        var newRoot = root.ReplaceNode(propertyDecl, updatedProperty);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>Extracts the first single-quoted token from the diagnostic message as the property name.</summary>
    /// <param name="diagnostic">The IM0001 diagnostic whose message contains the property name.</param>
    /// <returns>The property name, or <see langword="null"/> if the message format is not recognised.</returns>
    private static string? ExtractPropertyNameFromMessage(Diagnostic diagnostic)
    {
        // Message format: "Property '{0}' on destination type '{1}' has no corresponding…"
        var msg = diagnostic.GetMessage();
        var start = msg.IndexOf('\'');
        if (start < 0) return null;
        var end = msg.IndexOf('\'', start + 1);
        if (end <= start) return null;
        return msg.Substring(start + 1, end - start - 1);
    }
}
