using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Analyses classes that inherit from <c>MappingProfile</c> and extracts
/// <see cref="MappingDescriptor"/>s from the <c>CreateMap&lt;TSource, TDest&gt;()</c>
/// call chains found in their constructors.
/// </summary>
internal static class ProfileAnalyzer
{
    private const string MappingProfileFqn = "IronMapper.Configuration.MappingProfile";

    /// <summary>
    /// Returns true when the syntax node is a class declaration
    /// that might extend MappingProfile (quick filter before semantic check).
    /// File-scoped classes (<c>file class</c>) are excluded because they cannot be
    /// referenced from generated code.
    /// </summary>
    public static bool IsCandidateClass(SyntaxNode node, CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax { BaseList: not null } classDecl) return false;

        // Skip file-scoped types — they are invisible outside their file.
        foreach (var modifier in classDecl.Modifiers)
            if (modifier.IsKind(SyntaxKind.FileKeyword)) return false;

        return true;
    }

    /// <summary>
    /// Performs the full semantic check and extracts all mapping descriptors
    /// declared inside the profile constructor.  Returns an empty array when
    /// the class is not a MappingProfile subclass.
    /// </summary>
    public static ImmutableArray<MappingDescriptor> ExtractFromProfile(
        GeneratorSyntaxContext ctx,
        CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (classSymbol is null) return ImmutableArray<MappingDescriptor>.Empty;
        if (!InheritsFrom(classSymbol, MappingProfileFqn)) return ImmutableArray<MappingDescriptor>.Empty;

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();

        // Look for the primary (or any) constructor body.
        foreach (var ctor in classDecl.Members)
        {
            ct.ThrowIfCancellationRequested();

            if (ctor is not ConstructorDeclarationSyntax ctorDecl) continue;
            if (ctorDecl.Body is null) continue;

            // Walk all expression-statements in the constructor body looking for
            // CreateMap<Src, Dest>().ForMember(...).When(...) chains.
            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                if (statement is not ExpressionStatementSyntax exprStmt) continue;

                var descriptors = AnalyseChain(exprStmt.Expression, ctx.SemanticModel, ct);
                builder.AddRange(descriptors);
            }
        }

        // IM0005: profile class exists but defines no mappings.
        if (builder.Count == 0)
        {
            var sentinel = new MappingDescriptor(
                sourceTypeName: string.Empty,
                sourceNamespace: null,
                destTypeName: string.Empty,
                destNamespace: null,
                propertyMappings: ImmutableArray<PropertyMappingDescriptor>.Empty,
                diagnostics: ImmutableArray.Create(new DiagnosticInfo(
                    DiagnosticDescriptors.EmptyMappingProfile,
                    classSymbol.Name)),
                hasCustomConverter: false);
            return ImmutableArray.Create(sentinel);
        }

        return builder.ToImmutable();
    }

    // -----------------------------------------------------------------------
    // Chain analysis
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recursively unwraps an invocation chain such as:
    /// CreateMap&lt;Src,Dest&gt;().ForMember(...).Ignore(...).When(...).ReverseMap()
    /// Returns one (or two, for ReverseMap) descriptors.
    /// </summary>
    private static IReadOnlyList<MappingDescriptor> AnalyseChain(
        ExpressionSyntax expr,
        SemanticModel model,
        CancellationToken ct)
    {
        // Collect all method calls in the chain from outermost to innermost.
        var calls = new List<InvocationExpressionSyntax>();
        var current = expr;

        while (current is InvocationExpressionSyntax inv)
        {
            calls.Add(inv);
            var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
            current = memberAccess?.Expression;
        }

        // The innermost invocation should be CreateMap<Src,Dest>().
        if (calls.Count == 0) return System.Array.Empty<MappingDescriptor>();

        var innermost = calls[calls.Count - 1];
        var (sourceSymbol, destSymbol) = ExtractCreateMapTypes(innermost, model, ct);
        if (sourceSymbol is null || destSymbol is null) return System.Array.Empty<MappingDescriptor>();

        // Walk calls from innermost (CreateMap) outward, collecting config.
        var forMemberConfigs = new List<(string destProp, string? lambdaBody, bool isIgnore)>();
        string? whenConditionBody = null;
        bool reverseMap = false;

        // calls[Count-1] = CreateMap, calls[0] = outermost
        for (int i = calls.Count - 2; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var inv = calls[i];
            var memberAccess = (MemberAccessExpressionSyntax)inv.Expression;
            var methodName = memberAccess.Name.Identifier.Text;

            switch (methodName)
            {
                case "ForMember":
                    ParseForMember(inv, forMemberConfigs);
                    break;

                case "Ignore" when inv.ArgumentList.Arguments.Count == 1:
                    // Standalone .Ignore(dest => dest.Prop) — not inside ForMember
                    var ignoreDestProp = ExtractMemberName(inv.ArgumentList.Arguments[0].Expression);
                    if (ignoreDestProp is not null)
                        forMemberConfigs.Add((ignoreDestProp, null, true));
                    break;

                case "When" when inv.ArgumentList.Arguments.Count == 1:
                    whenConditionBody = ExtractLambdaBody(
                        inv.ArgumentList.Arguments[0].Expression, "source");
                    break;

                case "ReverseMap":
                    reverseMap = true;
                    break;
            }
        }

        var results = new List<MappingDescriptor>();

        var forward = BuildDescriptorFromProfile(
            sourceSymbol, destSymbol, forMemberConfigs, whenConditionBody, model, ct);
        if (forward is not null) results.Add(forward);

        if (reverseMap)
        {
            // Reverse: swap source/dest, use simple name matching (no ForMember customisations).
            var reverse = BuildDescriptorFromProfile(
                destSymbol, sourceSymbol,
                new List<(string, string?, bool)>(),
                whenCondition: null,
                model, ct);
            if (reverse is not null) results.Add(reverse);
        }

        return results;
    }

    private static void ParseForMember(
        InvocationExpressionSyntax inv,
        List<(string destProp, string? lambdaBody, bool isIgnore)> configs)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 2) return;

        var destPropName = ExtractMemberName(args[0].Expression);
        if (destPropName is null) return;

        // Second argument is Action<IMemberConfigurationExpression> — a lambda like opt => opt.MapFrom(...) or opt => opt.Ignore()
        var optLambda = args[1].Expression as LambdaExpressionSyntax;
        if (optLambda is null) return;

        var optBody = optLambda.Body as InvocationExpressionSyntax;
        if (optBody is null) return;

        var optMethod = (optBody.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;

        if (optMethod == "Ignore")
        {
            configs.Add((destPropName, null, true));
            return;
        }

        if (optMethod == "MapFrom" && optBody.ArgumentList.Arguments.Count == 1)
        {
            var mapFromArg = optBody.ArgumentList.Arguments[0].Expression;
            var body = ExtractLambdaBody(mapFromArg, "source");
            configs.Add((destPropName, body, false));
        }
    }

    // -----------------------------------------------------------------------
    // Descriptor builder
    // -----------------------------------------------------------------------

    private static MappingDescriptor? BuildDescriptorFromProfile(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        List<(string destProp, string? lambdaBody, bool isIgnore)> forMemberConfigs,
        string? whenCondition,
        SemanticModel model,
        CancellationToken ct)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // IM0008: destination type is abstract or an interface.
        if (destSymbol.IsAbstract || destSymbol.TypeKind == TypeKind.Interface)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AbstractDestinationType,
                destSymbol.Name));
            return new MappingDescriptor(
                sourceTypeName: sourceSymbol.Name,
                sourceNamespace: SymbolHelpers.GetNamespace(sourceSymbol),
                destTypeName: destSymbol.Name,
                destNamespace: SymbolHelpers.GetNamespace(destSymbol),
                propertyMappings: ImmutableArray<PropertyMappingDescriptor>.Empty,
                diagnostics: diagnostics.ToImmutable(),
                hasCustomConverter: false,
                whenConditionBody: null);
        }

        var sourceProps = SymbolHelpers.GetPublicReadableProperties(sourceSymbol);
        var destProps = SymbolHelpers.GetPublicSettableProperties(destSymbol);

        // Index source props by name (case-insensitive).
        var sourcePropsByName = new Dictionary<string, IPropertySymbol>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in sourceProps)
            sourcePropsByName[p.Name] = p;

        // Index ForMember configs by dest property name.
        var configByDest = new Dictionary<string, (string? lambdaBody, bool isIgnore)>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var (dp, lb, ign) in forMemberConfigs)
            configByDest[dp] = (lb, ign);

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            if (configByDest.TryGetValue(destProp.Name, out var cfg))
            {
                // Explicitly configured via ForMember.
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: destProp.Name, // fallback name
                    destPropertyName: destProp.Name,
                    isIgnored: cfg.isIgnore,
                    converterType: null,
                    needsNullCheck: false,
                    lambdaBody: cfg.lambdaBody));
                continue;
            }

            // Default: match by name.
            if (sourcePropsByName.TryGetValue(destProp.Name, out var namedProp))
            {
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: namedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: false,
                    converterType: null,
                    needsNullCheck: namedProp.Type.IsReferenceType));
            }
            // Unmatched destination properties are silently skipped in profile-based mapping.
        }

        return new MappingDescriptor(
            sourceTypeName: sourceSymbol.Name,
            sourceNamespace: SymbolHelpers.GetNamespace(sourceSymbol),
            destTypeName: destSymbol.Name,
            destNamespace: SymbolHelpers.GetNamespace(destSymbol),
            propertyMappings: propertyMappings.ToImmutable(),
            diagnostics: diagnostics.ToImmutable(),
            hasCustomConverter: false,
            whenConditionBody: whenCondition);
    }

    // -----------------------------------------------------------------------
    // Lambda body extraction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts the body text from a lambda expression and replaces the parameter
    /// name with <paramref name="replacementParam"/> so the generated code uses
    /// the canonical variable name (e.g. "source").
    /// </summary>
    private static string? ExtractLambdaBody(ExpressionSyntax expr, string replacementParam)
    {
        LambdaExpressionSyntax? lambda = expr as SimpleLambdaExpressionSyntax
                                      ?? (LambdaExpressionSyntax?)(expr as ParenthesizedLambdaExpressionSyntax);
        if (lambda is null) return null;

        string paramName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count > 0
                => paren.ParameterList.Parameters[0].Identifier.Text,
            _ => string.Empty
        };

        var bodyText = lambda.Body.ToString();

        // Replace "paramName." with "replacementParam." and bare "paramName" → rarely needed.
        if (!string.IsNullOrEmpty(paramName) && paramName != replacementParam)
        {
            bodyText = ReplaceIdentifier(bodyText, paramName, replacementParam);
        }

        return bodyText;
    }

    /// <summary>
    /// Extracts the member name from a lambda like <c>d =&gt; d.FullName</c>.
    /// Returns null if the expression is not in that form.
    /// </summary>
    private static string? ExtractMemberName(ExpressionSyntax expr)
    {
        var body = expr switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };

        // Handle casts: (object?)d.Prop → unwrap
        if (body is CastExpressionSyntax cast) body = cast.Expression;
        if (body is ParenthesizedExpressionSyntax paren2) body = paren2.Expression;

        return (body as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
    }

    // -----------------------------------------------------------------------
    // Type resolution
    // -----------------------------------------------------------------------

    private static (INamedTypeSymbol? source, INamedTypeSymbol? dest) ExtractCreateMapTypes(
        InvocationExpressionSyntax inv,
        SemanticModel model,
        CancellationToken ct)
    {
        // CreateMap<TSource, TDest>() — type args on the generic name.
        var methodSymbol = model.GetSymbolInfo(inv, ct).Symbol as IMethodSymbol;
        if (methodSymbol is null) return (null, null);
        if (methodSymbol.TypeArguments.Length < 2) return (null, null);

        var source = methodSymbol.TypeArguments[0] as INamedTypeSymbol;
        var dest = methodSymbol.TypeArguments[1] as INamedTypeSymbol;
        return (source, dest);
    }

    // -----------------------------------------------------------------------
    // Symbol helpers
    // -----------------------------------------------------------------------

    private static bool InheritsFrom(INamedTypeSymbol symbol, string baseTypeFqn)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseTypeFqn) return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Simple word-boundary replacement: replaces <paramref name="oldId"/> with
    /// <paramref name="newId"/> only when it appears as a complete identifier
    /// (not as part of a longer identifier), e.g. replaces "src" but not "srcProp".
    /// </summary>
    private static string ReplaceIdentifier(string text, string oldId, string newId)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            // Check if we're at the start of an occurrence of oldId.
            if (i + oldId.Length <= text.Length
                && text.Substring(i, oldId.Length) == oldId)
            {
                bool prefixOk = i == 0 || !IsIdentChar(text[i - 1]);
                bool suffixOk = i + oldId.Length == text.Length || !IsIdentChar(text[i + oldId.Length]);

                if (prefixOk && suffixOk)
                {
                    sb.Append(newId);
                    i += oldId.Length;
                    continue;
                }
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    private static bool IsIdentChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';
}
