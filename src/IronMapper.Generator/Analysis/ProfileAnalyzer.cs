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

            // Pass 1: collect AddTransformer declarations.
            var transformersByType = new Dictionary<string, ValueTransformerDescriptor>();
            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                if (statement is not ExpressionStatementSyntax exprStmt) continue;
                if (TryExtractTransformer(exprStmt.Expression, ctx.SemanticModel, ct, out var t) && t is not null)
                    transformersByType[t.FullyQualifiedTypeName] = t; // last registration wins per type
            }
            var transformers = ImmutableArray.CreateRange(transformersByType.Values);

            // Pass 2: process CreateMap chains.
            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                if (statement is not ExpressionStatementSyntax exprStmt) continue;

                var descriptors = AnalyseChain(exprStmt.Expression, ctx.SemanticModel, transformers, ct);
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
        ImmutableArray<ValueTransformerDescriptor> transformers,
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
        // Tuple: (destPropName, lambdaBody, isIgnore, perPropertyConverterType)
        var forMemberConfigs = new List<(string destProp, string? lambdaBody, bool isIgnore, string? converterType)>();
        string? whenConditionBody = null;
        string? wholeObjectConverterType = null;
        string? wholeObjectLambdaBody = null;
        string? beforeMapBody = null;
        string? afterMapBody = null;
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
                    ParseForMember(inv, forMemberConfigs, model, ct);
                    break;

                case "Ignore" when inv.ArgumentList.Arguments.Count == 1:
                    // Standalone .Ignore(dest => dest.Prop) — not inside ForMember
                    var ignoreDestProp = ExtractMemberName(inv.ArgumentList.Arguments[0].Expression);
                    if (ignoreDestProp is not null)
                        forMemberConfigs.Add((ignoreDestProp, null, true, null));
                    break;

                case "When" when inv.ArgumentList.Arguments.Count == 1:
                    whenConditionBody = ExtractLambdaBody(
                        inv.ArgumentList.Arguments[0].Expression, "source");
                    break;

                case "ConvertUsing":
                    if (inv.ArgumentList.Arguments.Count == 0)
                    {
                        // ConvertUsing<TConverter>() — extract generic type argument.
                        var methodSym = model.GetSymbolInfo(inv, ct).Symbol as IMethodSymbol;
                        if (methodSym?.TypeArguments.Length > 0
                            && methodSym.TypeArguments[0] is INamedTypeSymbol convSym)
                        {
                            var ns = SymbolHelpers.GetNamespace(convSym);
                            wholeObjectConverterType = ns is null
                                ? convSym.Name
                                : $"{ns}.{convSym.Name}";
                        }
                    }
                    else if (inv.ArgumentList.Arguments.Count == 1)
                    {
                        // ConvertUsing(src => ...) — extract lambda body.
                        wholeObjectLambdaBody = ExtractLambdaBody(
                            inv.ArgumentList.Arguments[0].Expression, "source");
                    }
                    break;

                case "BeforeMap" when inv.ArgumentList.Arguments.Count == 1:
                    beforeMapBody = ExtractTwoParamLambdaBody(
                        inv.ArgumentList.Arguments[0].Expression, "source", "destination");
                    break;

                case "AfterMap" when inv.ArgumentList.Arguments.Count == 1:
                    afterMapBody = ExtractTwoParamLambdaBody(
                        inv.ArgumentList.Arguments[0].Expression, "source", "destination");
                    break;

                case "ReverseMap":
                    reverseMap = true;
                    break;
            }
        }

        var results = new List<MappingDescriptor>();

        var forward = BuildDescriptorFromProfile(
            sourceSymbol, destSymbol, forMemberConfigs, whenConditionBody,
            wholeObjectConverterType, wholeObjectLambdaBody,
            beforeMapBody, afterMapBody, transformers, model, ct);
        if (forward is not null) results.Add(forward);

        if (reverseMap)
        {
            // Reverse: swap source/dest, use simple name matching (no ForMember/hook customisations).
            var reverse = BuildDescriptorFromProfile(
                destSymbol, sourceSymbol,
                new List<(string, string?, bool, string?)>(),
                whenCondition: null,
                wholeObjectConverterType: null,
                wholeObjectLambdaBody: null,
                beforeMapBody: null,
                afterMapBody: null,
                transformers, model, ct);
            if (reverse is not null) results.Add(reverse);
        }

        return results;
    }

    private static void ParseForMember(
        InvocationExpressionSyntax inv,
        List<(string destProp, string? lambdaBody, bool isIgnore, string? converterType)> configs,
        SemanticModel model,
        CancellationToken ct)
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
            configs.Add((destPropName, null, true, null));
            return;
        }

        if (optMethod == "UseConverter")
        {
            // UseConverter<TConverter>() — extract generic type argument.
            var methodSym = model.GetSymbolInfo(optBody, ct).Symbol as IMethodSymbol;
            if (methodSym?.TypeArguments.Length > 0
                && methodSym.TypeArguments[0] is INamedTypeSymbol convSym)
            {
                var ns = SymbolHelpers.GetNamespace(convSym);
                var converterTypeName = ns is null ? convSym.Name : $"{ns}.{convSym.Name}";
                configs.Add((destPropName, null, false, converterTypeName));
            }
            return;
        }

        if (optMethod == "MapFrom" && optBody.ArgumentList.Arguments.Count == 1)
        {
            var mapFromArg = optBody.ArgumentList.Arguments[0].Expression;
            var body = ExtractLambdaBody(mapFromArg, "source");
            configs.Add((destPropName, body, false, null));
        }
    }

    // -----------------------------------------------------------------------
    // Transformer extraction
    // -----------------------------------------------------------------------

    private static bool TryExtractTransformer(
        ExpressionSyntax expr,
        SemanticModel model,
        CancellationToken ct,
        out ValueTransformerDescriptor? result)
    {
        result = null;
        if (expr is not InvocationExpressionSyntax inv) return false;

        var methodName = inv.Expression switch
        {
            GenericNameSyntax gn => gn.Identifier.Text,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax mgn } => mgn.Identifier.Text,
            _ => null
        };
        if (methodName != "AddTransformer") return false;
        if (inv.ArgumentList.Arguments.Count != 1) return false;

        var methodSym = model.GetSymbolInfo(inv, ct).Symbol as IMethodSymbol;
        if (methodSym?.TypeArguments.Length != 1) return false;

        var typeArg = methodSym.TypeArguments[0];
        var fqn = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var friendlyName = typeArg.Name; // "String", "Decimal", etc.

        var lambdaBody = ExtractLambdaBody(inv.ArgumentList.Arguments[0].Expression, "value");
        if (lambdaBody is null) return false;

        result = new ValueTransformerDescriptor(
            fullyQualifiedTypeName: fqn,
            friendlyTypeName: friendlyName,
            lambdaBody: lambdaBody,
            methodName: $"TransformValue_{friendlyName}");
        return true;
    }

    // -----------------------------------------------------------------------
    // Descriptor builder
    // -----------------------------------------------------------------------

    private static MappingDescriptor? BuildDescriptorFromProfile(
        INamedTypeSymbol sourceSymbol,
        INamedTypeSymbol destSymbol,
        List<(string destProp, string? lambdaBody, bool isIgnore, string? converterType)> forMemberConfigs,
        string? whenCondition,
        string? wholeObjectConverterType,
        string? wholeObjectLambdaBody,
        string? beforeMapBody,
        string? afterMapBody,
        ImmutableArray<ValueTransformerDescriptor> transformers,
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
        var configByDest = new Dictionary<string, (string? lambdaBody, bool isIgnore, string? converterType)>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var (dp, lb, ign, cvt) in forMemberConfigs)
            configByDest[dp] = (lb, ign, cvt);

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            bool isInitOnly = destProp.SetMethod?.IsInitOnly ?? false;

            if (configByDest.TryGetValue(destProp.Name, out var cfg))
            {
                // Explicitly configured via ForMember.
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: destProp.Name, // fallback name
                    destPropertyName: destProp.Name,
                    isIgnored: cfg.isIgnore,
                    converterType: cfg.converterType,
                    needsNullCheck: false,
                    lambdaBody: cfg.lambdaBody,
                    isInitOnly: isInitOnly));
                continue;
            }

            // Default: match by name.
            if (sourcePropsByName.TryGetValue(destProp.Name, out var namedProp))
            {
                var (collectionMapMethod, collectionOutputType) =
                    DetectCollectionMapping(namedProp.Type, destProp.Type);
                var typeFqn = namedProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                propertyMappings.Add(new PropertyMappingDescriptor(
                    sourcePropertyName: namedProp.Name,
                    destPropertyName: destProp.Name,
                    isIgnored: false,
                    converterType: null,
                    needsNullCheck: namedProp.Type.IsReferenceType,
                    lambdaBody: null,
                    isInitOnly: isInitOnly,
                    collectionElementMapMethod: collectionMapMethod,
                    collectionOutputType: collectionOutputType,
                    sourcePropertyTypeFqn: typeFqn));
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
            whenConditionBody: whenCondition,
            wholeObjectConverterType: wholeObjectConverterType,
            wholeObjectLambdaBody: wholeObjectLambdaBody,
            beforeMapLambdaBody: beforeMapBody,
            afterMapLambdaBody: afterMapBody,
            valueTransformers: transformers);
    }

    /// <summary>
    /// When both <paramref name="sourcePropType"/> and <paramref name="destPropType"/> are
    /// collection types whose element types differ, returns the mapping method name and output kind.
    /// </summary>
    private static (string? mapMethod, string? outputType) DetectCollectionMapping(
        ITypeSymbol sourcePropType,
        ITypeSymbol destPropType)
    {
        if (!SymbolHelpers.TryGetCollectionElementType(sourcePropType, out var srcElem) || srcElem is null)
            return (null, null);
        if (!SymbolHelpers.TryGetCollectionElementType(destPropType, out var dstElem) || dstElem is null)
            return (null, null);
        if (srcElem.ToDisplayString() == dstElem.ToDisplayString())
            return (null, null);

        return ($"MapTo{dstElem.Name}", SymbolHelpers.GetCollectionOutputType(destPropType));
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
    /// Extracts the body from a two-parameter lambda such as
    /// <c>(src, dest) =&gt; dest.Prop = value</c> or
    /// <c>(src, dest) =&gt; { ... }</c>, renaming both parameters to
    /// <paramref name="param1Replacement"/> and <paramref name="param2Replacement"/>.
    /// For expression bodies the result is a single statement (semicolon appended).
    /// For block bodies the inner statements are returned verbatim (without outer braces).
    /// Returns <see langword="null"/> when the expression is not a two-parameter lambda.
    /// </summary>
    private static string? ExtractTwoParamLambdaBody(
        ExpressionSyntax expr,
        string param1Replacement,
        string param2Replacement)
    {
        if (expr is not ParenthesizedLambdaExpressionSyntax lambda) return null;
        if (lambda.ParameterList.Parameters.Count < 2) return null;

        var param1 = lambda.ParameterList.Parameters[0].Identifier.Text;
        var param2 = lambda.ParameterList.Parameters[1].Identifier.Text;

        string bodyText;
        if (lambda.Body is BlockSyntax block)
        {
            // Join all statements, trimming leading whitespace from each.
            var sb = new System.Text.StringBuilder();
            foreach (var stmt in block.Statements)
            {
                sb.AppendLine(stmt.ToString().TrimStart());
            }
            bodyText = sb.ToString().TrimEnd();
        }
        else
        {
            // Expression body — turn into a statement by appending a semicolon.
            bodyText = lambda.Body.ToString() + ";";
        }

        if (!string.IsNullOrEmpty(param1) && param1 != param1Replacement)
            bodyText = ReplaceIdentifier(bodyText, param1, param1Replacement);
        if (!string.IsNullOrEmpty(param2) && param2 != param2Replacement)
            bodyText = ReplaceIdentifier(bodyText, param2, param2Replacement);

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
