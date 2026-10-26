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
using Microsoft.CodeAnalysis.Simplification;

namespace IronMapper.CodeFixes;

/// <summary>
/// Code fix for IM0002 "no mapping for the pair" — a nested object or the elements of a collection whose type pair has no
/// mapping. The fix adds the mapping where the mapping with the diagnostic is declared: <c>CreateMap&lt;S, D&gt;();</c>
/// after the statement of the <c>CreateMap</c>/<c>ReverseMap()</c> call in a profile, <c>[MapTo(typeof(D))]</c> on the
/// source type of an attribute mapping (or <c>[MapFrom(typeof(S))]</c> on the destination type when the source type is
/// not in source code). The pair comes from the diagnostic properties, never from the message. Other IM0002 (incompatible
/// types, a nullable nested object for a non-nullable destination) have no such properties and no fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IM0002CodeFixProvider))]
[Shared]
public sealed class IM0002CodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "IM0002";
    private const string SourceIdKey = "PairSourceId";
    private const string DestinationIdKey = "PairDestinationId";
    private const string SourceTypeKey = "PairSourceType";
    private const string DestinationTypeKey = "PairDestinationType";
    private const string OriginKey = "MappingOrigin";
    private const string AttributesNamespace = "IronMapper.Attributes";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

    /// <inheritdoc/>
    /// <remarks>No Fix All: two diagnostics of one pair would add the mapping twice (IM0007).</remarks>
    public override FixAllProvider? GetFixAllProvider() => null;

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
            if (Get(diagnostic, SourceTypeKey) is not { } source || Get(diagnostic, DestinationTypeKey) is not { } dest
                || Get(diagnostic, OriginKey) is not { } origin)
            {
                continue;
            }

            if (origin is "Profile" or "ReverseMap")
            {
                if (FindProfileStatement(root, diagnostic) is not { } statement)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Add CreateMap<{Display(source)}, {Display(dest)}>() to the profile",
                        createChangedDocument: _ => Task.FromResult(AddCreateMap(context.Document, root, statement, source, dest)),
                        equivalenceKey: DiagnosticId + "_AddCreateMap"),
                    diagnostic);
                continue;
            }

            var compilation = await context.Document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            // [MapTo(typeof(D))] on the source type, else [MapFrom(typeof(S))] on the destination type.
            if (FindDeclaration(compilation, Get(diagnostic, SourceIdKey)) is { } sourceDeclaration)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Add [MapTo(typeof({Display(dest)}))] to '{Display(source)}'",
                        createChangedSolution: ct => AddAttributeAsync(context.Document.Project.Solution, sourceDeclaration, "MapTo", dest, ct),
                        equivalenceKey: DiagnosticId + "_AddMapTo"),
                    diagnostic);
            }
            else if (FindDeclaration(compilation, Get(diagnostic, DestinationIdKey)) is { } destDeclaration)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Add [MapFrom(typeof({Display(source)}))] to '{Display(dest)}'",
                        createChangedSolution: ct => AddAttributeAsync(context.Document.Project.Solution, destDeclaration, "MapFrom", source, ct),
                        equivalenceKey: DiagnosticId + "_AddMapFrom"),
                    diagnostic);
            }
        }
    }

    private static string? Get(Diagnostic diagnostic, string key)
        => diagnostic.Properties.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    /// <summary>The statement of the <c>CreateMap</c>/<c>ReverseMap()</c> call the diagnostic points to, in a block.</summary>
    private static StatementSyntax? FindProfileStatement(SyntaxNode root, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        return node.FirstAncestorOrSelf<StatementSyntax>() is { Parent: BlockSyntax } statement ? statement : null;
    }

    /// <summary><c>CreateMap&lt;S, D&gt;();</c> after <paramref name="statement"/>, with its indentation; the names simplified.</summary>
    private static Document AddCreateMap(Document document, SyntaxNode root, StatementSyntax statement, string source, string dest)
    {
        var createMap = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.GenericName(SyntaxFactory.Identifier("CreateMap"),
                    SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { TypeName(source), TypeName(dest) })))));

        var indentation = statement.GetLeadingTrivia().Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).LastOrDefault();
        var endOfLine = statement.GetTrailingTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        createMap = createMap
            .WithLeadingTrivia(indentation == default ? SyntaxFactory.TriviaList() : SyntaxFactory.TriviaList(indentation))
            .WithTrailingTrivia(endOfLine == default ? SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed) : SyntaxFactory.TriviaList(endOfLine));

        return document.WithSyntaxRoot(root.InsertNodesAfter(statement, new[] { createMap }));
    }

    /// <summary>Adds <c>[MapTo(typeof(T))]</c>/<c>[MapFrom(typeof(T))]</c> to <paramref name="declaration"/> (and the using it needs).</summary>
    private static async Task<Solution> AddAttributeAsync(Solution solution, SyntaxReference declaration, string attribute,
        string type, CancellationToken ct)
    {
        var document = solution.GetDocument(declaration.SyntaxTree);
        if (document is null)
        {
            return solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null || await declaration.GetSyntaxAsync(ct).ConfigureAwait(false) is not TypeDeclarationSyntax typeDeclaration)
        {
            return solution;
        }

        var list = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attribute),
                    SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(TypeName(type))))))))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(typeDeclaration, typeDeclaration.AddAttributeLists(list));

        if (!await AttributeIsInScopeAsync(document, typeDeclaration, attribute, ct).ConfigureAwait(false)
            && newRoot is CompilationUnitSyntax unit)
        {
            newRoot = unit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(AttributesNamespace))
                .WithAdditionalAnnotations(Formatter.Annotation));
        }

        return solution.WithDocumentSyntaxRoot(document.Id, newRoot);
    }

    /// <summary>Whether <paramref name="attribute"/> already binds to the IronMapper attribute at the type declaration.</summary>
    private static async Task<bool> AttributeIsInScopeAsync(Document document, SyntaxNode node, string attribute, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model is null)
        {
            return true;
        }

        var type = model.GetSpeculativeTypeInfo(node.SpanStart, SyntaxFactory.ParseTypeName(attribute + "Attribute"),
            SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        return type?.ToDisplayString() == $"{AttributesNamespace}.{attribute}Attribute";
    }

    /// <summary>The first declaration in source of the type with the documentation comment ID <paramref name="id"/>.</summary>
    private static SyntaxReference? FindDeclaration(Compilation compilation, string? id)
        => id is not null && DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) is INamedTypeSymbol type
            ? type.DeclaringSyntaxReferences.FirstOrDefault()
            : null;

    /// <summary>A fully qualified type name (<c>global::App.Address</c>), simplified when the fix is applied.</summary>
    private static TypeSyntax TypeName(string fullName)
        => SyntaxFactory.ParseTypeName(fullName).WithAdditionalAnnotations(Simplifier.Annotation);

    private static string Display(string fullName)
        => fullName.StartsWith("global::", System.StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;
}
