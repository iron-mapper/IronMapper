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
using Microsoft.CodeAnalysis.Formatting;

namespace IronMapper.CodeFixes;

/// <summary>
/// Code fix for IM0001 — unmapped destination property. The diagnostic points to the destination property of an
/// attribute mapping (the fix adds <c>[Ignore]</c> to it) or to the <c>CreateMap</c> call of a profile mapping (the fix
/// adds <c>.ForMember(dest =&gt; dest.X, opt =&gt; opt.Ignore())</c>: a profile reads <c>[Ignore]</c> from 2.1.0, but it would
/// apply to every mapping to the type, while <c>ForMember</c> ignores the property in this mapping only).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IM0001CodeFixProvider))]
[Shared]
public sealed class IM0001CodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "IM0001";
    private const string PropertyNameKey = "PropertyName";
    private const string AddIgnoreKey = DiagnosticId + "_AddIgnore";
    private const string ForMemberIgnoreKey = DiagnosticId + "_ForMemberIgnore";
    private const string AttributesNamespace = "IronMapper.Attributes";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticId);

    /// <inheritdoc/>
    /// <remarks>
    /// Several diagnostics of one profile mapping change the same <c>CreateMap</c> call, which the batch fixer would
    /// treat as conflicting changes, so all diagnostics of a document are fixed in one pass.
    /// </remarks>
    public override FixAllProvider GetFixAllProvider()
        => FixAllProvider.Create(static async (context, document, diagnostics)
            => await FixAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            string? name = GetPropertyName(diagnostic);
            if (name is null)
            {
                continue;
            }

            switch (FindTarget(root, diagnostic))
            {
                case PropertyDeclarationSyntax or ParameterSyntax:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: $"Add [Ignore] to '{name}'",
                            createChangedDocument: ct => FixAsync(context.Document, ImmutableArray.Create(diagnostic), ct),
                            equivalenceKey: AddIgnoreKey),
                        diagnostic);
                    break;

                case InvocationExpressionSyntax:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: $"Ignore '{name}' in this mapping (ForMember)",
                            createChangedDocument: ct => FixAsync(context.Document, ImmutableArray.Create(diagnostic), ct),
                            equivalenceKey: ForMemberIgnoreKey),
                        diagnostic);
                    break;
            }
        }
    }

    /// <summary>Fixes <paramref name="diagnostics"/> of <paramref name="document"/>; returns the document unchanged if none applies.</summary>
    private static async Task<Document> FixAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Destination properties to mark, and profile mappings with the properties to ignore in them.
        var ignored = new HashSet<SyntaxNode>();
        var forMembers = new Dictionary<ExpressionSyntax, List<string>>();
        foreach (var diagnostic in diagnostics)
        {
            string? name = GetPropertyName(diagnostic);
            if (name is null)
            {
                continue;
            }

            switch (FindTarget(root, diagnostic))
            {
                case PropertyDeclarationSyntax property:
                    ignored.Add(property);
                    break;
                case ParameterSyntax parameter:
                    ignored.Add(parameter);
                    break;
                case InvocationExpressionSyntax createMap:
                    var anchor = LastForMember(createMap);
                    if (!forMembers.TryGetValue(anchor, out var names))
                    {
                        forMembers[anchor] = names = new List<string>();
                    }

                    if (!names.Contains(name))
                    {
                        names.Add(name);
                    }

                    break;
            }
        }

        if (ignored.Count == 0 && forMembers.Count == 0)
        {
            return document;
        }

        var newRoot = root.ReplaceNodes(ignored.Concat(forMembers.Keys), (original, _) => original switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(IgnoreAttribute(propertyTarget: false)),
            ParameterSyntax parameter => parameter.AddAttributeLists(IgnoreAttribute(propertyTarget: true)),
            ExpressionSyntax anchor when forMembers.TryGetValue(anchor, out var names) => AddForMemberIgnore(anchor, names),
            _ => original,
        });

        if (ignored.Count > 0 && !await IgnoreAttributeIsInScopeAsync(document, root, ignored.First(), ct).ConfigureAwait(false)
            && newRoot is CompilationUnitSyntax unit)
        {
            newRoot = unit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(AttributesNamespace))
                .WithAdditionalAnnotations(Formatter.Annotation));
        }

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary><c>[Ignore]</c>, or <c>[property: Ignore]</c> for a positional record parameter.</summary>
    private static AttributeListSyntax IgnoreAttribute(bool propertyTarget)
    {
        var list = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Ignore"))));
        if (propertyTarget)
        {
            list = list.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.PropertyKeyword)));
        }

        return list.WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    /// The <c>CreateMap</c> call, or the last <c>.ForMember(...)</c> chained directly to it: new <c>ForMember</c> calls go
    /// there, so one fix at a time and Fix All add them in the same order, and before <c>ReverseMap()</c> (in AutoMapper a
    /// <c>ForMember</c> after <c>ReverseMap()</c> configures the reverse mapping).
    /// </summary>
    private static ExpressionSyntax LastForMember(InvocationExpressionSyntax createMap)
    {
        ExpressionSyntax anchor = createMap;
        while (anchor.Parent is MemberAccessExpressionSyntax { Name.Identifier.Text: "ForMember" } access
               && access.Expression == anchor
               && access.Parent is InvocationExpressionSyntax forMember)
        {
            anchor = forMember;
        }

        return anchor;
    }

    /// <summary><c>….ForMember(dest =&gt; dest.X, opt =&gt; opt.Ignore())</c> appended to <paramref name="anchor"/> for each name.</summary>
    private static ExpressionSyntax AddForMemberIgnore(ExpressionSyntax anchor, List<string> names)
    {
        ExpressionSyntax result = anchor.WithoutTrailingTrivia();
        foreach (string name in names)
        {
            result = SyntaxFactory.ParseExpression(
                $"{result}.ForMember(dest => dest.{name}, opt => opt.Ignore())");
        }

        return result.WithTriviaFrom(anchor).WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>Whether <c>Ignore</c> already binds to <c>IronMapper.Attributes.IgnoreAttribute</c> at <paramref name="node"/>.</summary>
    private static async Task<bool> IgnoreAttributeIsInScopeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model is null)
        {
            return true;
        }

        int position = root.FindNode(node.Span).SpanStart;
        var type = model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName("IgnoreAttribute"),
            SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        return type?.ToDisplayString() == AttributesNamespace + ".IgnoreAttribute";
    }

    /// <summary>
    /// The node the diagnostic points to: the destination property (or record parameter) of an attribute mapping, or the
    /// <c>CreateMap</c> call of a profile mapping. <see langword="null"/> for other locations (a destination type from
    /// another assembly: the diagnostic points to the <c>[MapTo]</c>/<c>[MapFrom]</c> attribute and there is nothing to fix).
    /// </summary>
    private static SyntaxNode? FindTarget(SyntaxNode root, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        return node.AncestorsAndSelf().FirstOrDefault(n => n is PropertyDeclarationSyntax or ParameterSyntax
            || (n is InvocationExpressionSyntax invocation && n.Span == diagnostic.Location.SourceSpan && IsCreateMap(invocation))
            || n is AttributeSyntax or TypeDeclarationSyntax) switch
        {
            AttributeSyntax or TypeDeclarationSyntax => null,
            var target => target,
        };
    }

    private static bool IsCreateMap(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        GenericNameSyntax { Identifier.Text: "CreateMap" } => true,
        MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.Text: "CreateMap" } } => true,
        _ => false,
    };

    private static string? GetPropertyName(Diagnostic diagnostic)
        => diagnostic.Properties.TryGetValue(PropertyNameKey, out string? name) && !string.IsNullOrEmpty(name) ? name : null;
}
