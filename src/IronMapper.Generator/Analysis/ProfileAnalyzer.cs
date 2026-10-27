using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Analyses classes that inherit from <c>MappingProfile</c> and extracts
/// <see cref="MappingDescriptor"/>s from the <c>CreateMap&lt;TSource, TDest&gt;()</c>
/// call chains found in their constructors.
/// </summary>
internal static class ProfileAnalyzer
{
    private const string MappingProfileFqn = "IronMapper.Configuration.MappingProfile";

    // Canonical parameter names used by the emitted code (see MapperCodeEmitter).
    private static readonly string[] SourceParameter = { "source" };
    private static readonly string[] HookParameters = { "source", "destination" };
    private static readonly string[] TransformerParameter = { "value" };

    /// <summary>
    /// Returns true when the syntax node is a class declaration
    /// that might extend MappingProfile (quick filter before semantic check).
    /// File-scoped classes (<c>file class</c>) are excluded because they cannot be
    /// referenced from generated code.
    /// </summary>
    public static bool IsCandidateClass(SyntaxNode node, CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax { BaseList: not null } classDecl)
        {
            return false;
        }

        // Skip file-scoped types — they are invisible outside their file.
        foreach (var modifier in classDecl.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Performs the full semantic check and extracts all mapping descriptors
    /// declared inside the profile constructor.  Returns an empty array when
    /// the class is not a MappingProfile subclass.
    /// </summary>
    public static EquatableArray<MappingDescriptor> ExtractFromProfile(
        GeneratorSyntaxContext ctx,
        CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (classSymbol is null)
        {
            return ImmutableArray<MappingDescriptor>.Empty;
        }

        if (!InheritsFrom(classSymbol, MappingProfileFqn))
        {
            return ImmutableArray<MappingDescriptor>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MappingDescriptor>();
        string profileName = classSymbol.ToDisplayString();

        // Look for the primary (or any) constructor body.
        int ctorIndex = -1;
        // A CreateMap whose types did not resolve is skipped, but the profile is not empty (no IM0005).
        bool hasSkippedMapping = false;
        foreach (var ctor in classDecl.Members)
        {
            ct.ThrowIfCancellationRequested();

            if (ctor is not ConstructorDeclarationSyntax ctorDecl)
            {
                continue;
            }

            if (ctorDecl.Body is null)
            {
                continue;
            }

            ctorIndex++;

            // Pass 1: collect AddTransformer declarations.
            var transformersByType = new Dictionary<string, ValueTransformerDescriptor>();
            var transformerDiagnostics = new List<DiagnosticInfo>();
            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                if (statement is not ExpressionStatementSyntax exprStmt)
                {
                    continue;
                }

                if (TryExtractTransformer(exprStmt.Expression, ctx.SemanticModel, profileName, transformerDiagnostics, ct, out var t)
                    && t is not null)
                {
                    transformersByType[t.FullyQualifiedTypeName] = t; // last registration wins per type
                }
            }
            var transformers = ImmutableArray.CreateRange(transformersByType.Values);

            // A transformer that cannot be emitted is an error (IM0011/IM0012); the mappings are still generated.
            if (transformerDiagnostics.Count > 0)
            {
                builder.Add(MappingAnalyzer.CreateDiagnosticOnlyDescriptor(ImmutableArray.CreateRange(transformerDiagnostics)));
            }

            // Pass 2: process CreateMap chains.
            int statementIndex = -1;
            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                statementIndex++;
                if (statement is not ExpressionStatementSyntax exprStmt)
                {
                    continue;
                }

                // Stable key of this CreateMap call: profile, constructor, statement (zero-padded for ordinal sorting).
                string origin = $"profile:{profileName}#{ctorIndex:D4}.{statementIndex:D4}";
                var descriptors = AnalyseChain(
                    exprStmt.Expression, ctx.SemanticModel, transformers, profileName, origin, out bool skipped, ct);
                builder.AddRange(descriptors);
                hasSkippedMapping |= skipped;
            }
        }

        // IM0005: profile class exists but defines no mappings.
        if (builder.Count == 0 && !hasSkippedMapping)
        {
            var sentinel = new MappingDescriptor(
                sourceTypeName: string.Empty,
                sourceNamespace: null,
                destTypeName: string.Empty,
                destNamespace: null,
                propertyMappings: ImmutableArray<PropertyMappingDescriptor>.Empty,
                diagnostics: ImmutableArray.Create(new DiagnosticInfo(
                    DiagnosticDescriptors.EmptyMappingProfile,
                    classSymbol.Name).WithLocation(LocationInfo.From(classDecl.Identifier.GetLocation()))),
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
    /// Returns one (or two, for ReverseMap) descriptors. <paramref name="skippedUnresolved"/> is set when a mapping of the
    /// chain is skipped because a type did not resolve (the compiler reports it at the user's code).
    /// </summary>
    private static IReadOnlyList<MappingDescriptor> AnalyseChain(
        ExpressionSyntax expr,
        SemanticModel model,
        ImmutableArray<ValueTransformerDescriptor> transformers,
        string profileName,
        string origin,
        out bool skippedUnresolved,
        CancellationToken ct)
    {
        skippedUnresolved = false;

        // Problems found while rewriting lambdas (IM0011/IM0012); any problem skips the whole chain.
        var lambdaDiagnostics = new List<DiagnosticInfo>();

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
        if (calls.Count == 0)
        {
            return System.Array.Empty<MappingDescriptor>();
        }

        var innermost = calls[calls.Count - 1];
        var (sourceSymbol, destSymbol) = ExtractCreateMapTypes(innermost, model, out skippedUnresolved, ct);
        if (sourceSymbol is null || destSymbol is null)
        {
            return System.Array.Empty<MappingDescriptor>();
        }

        // Walk calls from innermost (CreateMap) outward, collecting config.
        // Tuple: (destPropName, lambdaBody, isIgnore, perPropertyConverterType, the type of the MapFrom expression)
        var forMemberConfigs = new List<(string destProp, string? lambdaBody, bool isIgnore, ConverterReference? converter, MapFromType? mapFrom)>();
        var includedMembers = new List<string>();
        string? whenConditionBody = null;
        ConverterReference? wholeObjectConverter = null;
        string? wholeObjectLambdaBody = null;
        string? beforeMapBody = null;
        string? afterMapBody = null;
        bool reverseMap = false;
        // GenerateMethods(...): overloads of this mapping; a non-constant argument is IM0016 and keeps the assembly value.
        var methodOverrides = default(GeneratedMethodOverrides);
        var optionDiagnostics = new List<DiagnosticInfo>();
        // Where the mappings are defined: diagnostics of the whole mapping point here.
        var createMapLocation = LocationInfo.From(innermost);
        LocationInfo? reverseMapLocation = null;

        // calls[Count-1] = CreateMap, calls[0] = outermost
        for (int i = calls.Count - 2; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var inv = calls[i];
            var memberAccess = (MemberAccessExpressionSyntax)inv.Expression;
            string methodName = memberAccess.Name.Identifier.Text;

            switch (methodName)
            {
                case "ForMember":
                    ParseForMember(inv, forMemberConfigs, model, profileName, lambdaDiagnostics, ct);
                    break;

                case "Ignore" when inv.ArgumentList.Arguments.Count == 1:
                    // Standalone .Ignore(dest => dest.Prop) — not inside ForMember
                    string? ignoreDestProp = ExtractMemberName(inv.ArgumentList.Arguments[0].Expression);
                    if (ignoreDestProp is not null)
                    {
                        forMemberConfigs.Add((ignoreDestProp, null, true, null, null));
                    }

                    break;

                case "When" when inv.ArgumentList.Arguments.Count == 1:
                    whenConditionBody = LambdaBodyRewriter.RewriteBody(
                        inv.ArgumentList.Arguments[0].Expression, model, SourceParameter, profileName, lambdaDiagnostics, ct);
                    break;

                case "ConvertUsing":
                    if (inv.ArgumentList.Arguments.Count == 0)
                    {
                        // ConvertUsing<TConverter>() — extract generic type argument.
                        wholeObjectConverter = GetConverterTypeArgument(inv, model, ct);
                    }
                    else if (inv.ArgumentList.Arguments.Count == 1)
                    {
                        // ConvertUsing(src => ...) — extract lambda body.
                        wholeObjectLambdaBody = LambdaBodyRewriter.RewriteBody(
                            inv.ArgumentList.Arguments[0].Expression, model, SourceParameter, profileName, lambdaDiagnostics, ct);
                    }
                    break;

                case "BeforeMap" when inv.ArgumentList.Arguments.Count == 1:
                    beforeMapBody = LambdaBodyRewriter.RewriteAsStatements(
                        inv.ArgumentList.Arguments[0].Expression, model, HookParameters, profileName, lambdaDiagnostics, ct);
                    break;

                case "AfterMap" when inv.ArgumentList.Arguments.Count == 1:
                    afterMapBody = LambdaBodyRewriter.RewriteAsStatements(
                        inv.ArgumentList.Arguments[0].Expression, model, HookParameters, profileName, lambdaDiagnostics, ct);
                    break;

                case "IncludeMembers":
                    ParseIncludeMembers(inv, includedMembers);
                    break;

                case "GenerateMethods":
                    methodOverrides = ParseGenerateMethods(inv, model, profileName, optionDiagnostics, ct);
                    break;

                case "ReverseMap":
                    reverseMap = true;
                    reverseMapLocation = LocationInfo.From(memberAccess.Name.GetLocation());
                    break;
            }
        }

        if (lambdaDiagnostics.Count > 0)
        {
            return new[] { MappingAnalyzer.CreateDiagnosticOnlyDescriptor(ImmutableArray.CreateRange(lambdaDiagnostics)) };
        }

        var results = new List<MappingDescriptor>();

        var includedMembersArray = includedMembers.Count > 0
            ? ImmutableArray.CreateRange(includedMembers)
            : ImmutableArray<string>.Empty;

        var forward = BuildDescriptorFromProfile(
            sourceSymbol, destSymbol, forMemberConfigs, includedMembersArray, whenConditionBody,
            wholeObjectConverter, wholeObjectLambdaBody,
            beforeMapBody, afterMapBody, transformers, profileName, createMapLocation,
            // ConvertUsing replaces the whole mapping: no property is left unmapped.
            reportUnmapped: wholeObjectConverter is null && wholeObjectLambdaBody is null,
            PropertyTypeCheck.Origin.Profile, model, ct);
        // Null only when a converter type did not resolve.
        if (forward is null)
        {
            skippedUnresolved = true;
        }
        else
        {
            results.Add(forward.WithOrigin(
                origin, $"MappingProfile '{profileName}'", originRank: 0, SymbolHelpers.GetDestinationNaming(destSymbol),
                createMapLocation).WithMethodOverrides(methodOverrides));
        }

        // IM0016 does not stop the mapping: it is generated with the assembly value for that overload.
        if (optionDiagnostics.Count > 0)
        {
            results.Add(MappingAnalyzer.CreateDiagnosticOnlyDescriptor(ImmutableArray.CreateRange(optionDiagnostics)));
        }

        if (reverseMap)
        {
            // Reverse: swap source/dest, use simple name matching (no ForMember/hook customisations).
            // IncludeMembers is intentionally not propagated to the reverse mapping.
            var reverse = BuildDescriptorFromProfile(
                destSymbol, sourceSymbol,
                new List<(string, string?, bool, ConverterReference?, MapFromType?)>(),
                ImmutableArray<string>.Empty,
                whenCondition: null,
                wholeObjectConverter: null,
                wholeObjectLambdaBody: null,
                beforeMapBody: null,
                afterMapBody: null,
                transformers, profileName, reverseMapLocation ?? createMapLocation,
                // Not checked, as in AutoMapper (a reverse map is created with MemberList.None): it maps by name only and
                // ForMember does not apply to it. Declare it with CreateMap to have it checked.
                reportUnmapped: false,
                PropertyTypeCheck.Origin.ReverseMap, model, ct);
            if (reverse is not null)
            {
                results.Add(reverse.WithOrigin(
                    origin + ":reverse", $"ReverseMap() in MappingProfile '{profileName}'", originRank: 1,
                    SymbolHelpers.GetDestinationNaming(sourceSymbol), reverseMapLocation ?? createMapLocation));
            }
        }

        return results;
    }

    private static void ParseForMember(
        InvocationExpressionSyntax inv,
        List<(string destProp, string? lambdaBody, bool isIgnore, ConverterReference? converter, MapFromType? mapFrom)> configs,
        SemanticModel model,
        string profileName,
        List<DiagnosticInfo> lambdaDiagnostics,
        CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 2)
        {
            return;
        }

        string? destPropName = ExtractMemberName(args[0].Expression);
        if (destPropName is null)
        {
            return;
        }

        // Second argument is Action<IMemberConfigurationExpression> — a lambda like opt => opt.MapFrom(...) or opt => opt.Ignore()
        if (args[1].Expression is not LambdaExpressionSyntax optLambda)
        {
            return;
        }

        if (optLambda.Body is not InvocationExpressionSyntax optBody)
        {
            return;
        }

        string? optMethod = (optBody.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;

        if (optMethod == "Ignore")
        {
            configs.Add((destPropName, null, true, null, null));
            return;
        }

        if (optMethod == "UseConverter")
        {
            // UseConverter<TConverter>() — extract generic type argument.
            var converter = GetConverterTypeArgument(optBody, model, ct);
            if (converter is not null)
            {
                configs.Add((destPropName, null, false, converter, null));
            }

            return;
        }

        if (optMethod == "MapFrom" && optBody.ArgumentList.Arguments.Count == 1)
        {
            var mapFromArg = optBody.ArgumentList.Arguments[0].Expression;
            string? body = LambdaBodyRewriter.RewriteBody(mapFromArg, model, SourceParameter, profileName, lambdaDiagnostics, ct);
            // The type of the expression the lambda returns (IM0002 when it cannot be assigned to the property).
            var expression = (mapFromArg as LambdaExpressionSyntax)?.ExpressionBody;
            var mapFrom = expression is null
                ? (MapFromType?)null
                : new MapFromType(model.GetTypeInfo(expression, ct).Type, LocationInfo.From(expression));
            configs.Add((destPropName, body, false, null, mapFrom));
        }
    }

    /// <summary>
    /// The type argument of <c>UseConverter&lt;T&gt;()</c> or <c>ConvertUsing&lt;T&gt;()</c>. When the call does not bind
    /// (the type argument does not resolve), the type is read from the type argument itself, so the mapping can be skipped
    /// instead of generated without its converter.
    /// </summary>
    private static ConverterReference? GetConverterTypeArgument(InvocationExpressionSyntax inv, SemanticModel model, CancellationToken ct)
    {
        // IM0015 points to the type argument, or to the call when it cannot be found.
        var typeArgument = inv.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } name }
            ? name.TypeArgumentList.Arguments[0]
            : null;
        var location = LocationInfo.From((SyntaxNode?)typeArgument ?? inv);

        if (model.GetSymbolInfo(inv, ct).Symbol is IMethodSymbol { TypeArguments.Length: > 0 } method)
        {
            return method.TypeArguments[0] is INamedTypeSymbol type ? new ConverterReference(type, location) : null;
        }

        return typeArgument is not null && model.GetTypeInfo(typeArgument, ct).Type is INamedTypeSymbol resolved
            ? new ConverterReference(resolved, location)
            : null;
    }

    /// <summary>The type of a <c>MapFrom</c> expression and where it is written (IM0002).</summary>
    private readonly struct MapFromType
    {
        public MapFromType(ITypeSymbol? type, LocationInfo? location)
        {
            Type = type;
            Location = location;
        }

        public ITypeSymbol? Type { get; }

        public LocationInfo? Location { get; }
    }

    /// <summary>A converter type of <c>UseConverter&lt;T&gt;()</c>/<c>ConvertUsing&lt;T&gt;()</c> and where it is written.</summary>
    private readonly struct ConverterReference
    {
        public ConverterReference(INamedTypeSymbol type, LocationInfo? location)
        {
            Type = type;
            Location = location;
        }

        public INamedTypeSymbol Type { get; }

        public LocationInfo? Location { get; }
    }

    /// <summary>
    /// The arguments of <c>GenerateMethods(inPlace, list, array)</c>: a constant <see langword="true"/>/<see langword="false"/>
    /// sets the overload, <see langword="null"/> or an omitted argument keeps the assembly value. Any other expression
    /// cannot be read at compile time: IM0016 at the argument, and the assembly value applies.
    /// </summary>
    private static GeneratedMethodOverrides ParseGenerateMethods(InvocationExpressionSyntax inv, SemanticModel model,
        string profileName, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        if (model.GetOperation(inv, ct) is not IInvocationOperation operation)
        {
            return default;
        }

        bool? inPlace = null, list = null, array = null;
        foreach (var argument in operation.Arguments)
        {
            if (argument.ArgumentKind == ArgumentKind.DefaultValue || argument.Parameter is null)
            {
                continue;
            }

            string name = argument.Parameter.Name;
            if (name is not ("inPlace" or "list" or "array"))
            {
                continue;
            }

            // The argument is converted to bool?; its constant value is that of the expression the user wrote.
            var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;
            if (!value.ConstantValue.HasValue || value.ConstantValue.Value is not (null or bool))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NonConstantGenerateMethodsArgument,
                        name, profileName, argument.Syntax.ToString())
                    .WithLocation(LocationInfo.From(argument.Syntax)));
                continue;
            }

            bool? set = (bool?)value.ConstantValue.Value;
            switch (name)
            {
                case "inPlace": inPlace = set; break;
                case "list": list = set; break;
                default: array = set; break;
            }
        }

        return new GeneratedMethodOverrides(inPlace, list, array);
    }

    private static void ParseIncludeMembers(
        InvocationExpressionSyntax inv,
        List<string> includedMembers)
    {
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            string? memberName = ExtractMemberName(arg.Expression);
            if (memberName is not null)
            {
                includedMembers.Add(memberName);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Transformer extraction
    // -----------------------------------------------------------------------

    private static bool TryExtractTransformer(
        ExpressionSyntax expr,
        SemanticModel model,
        string profileName,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out ValueTransformerDescriptor? result)
    {
        result = null;
        if (expr is not InvocationExpressionSyntax inv)
        {
            return false;
        }

        string? methodName = inv.Expression switch
        {
            GenericNameSyntax gn => gn.Identifier.Text,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax mgn } => mgn.Identifier.Text,
            _ => null
        };
        if (methodName != "AddTransformer")
        {
            return false;
        }

        if (inv.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var methodSym = model.GetSymbolInfo(inv, ct).Symbol as IMethodSymbol;
        if (methodSym?.TypeArguments.Length != 1)
        {
            return false;
        }

        var typeArg = methodSym.TypeArguments[0];
        string fqn = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string friendlyName = typeArg.Name; // "String", "Decimal", etc.

        string? lambdaBody = LambdaBodyRewriter.RewriteBody(
            inv.ArgumentList.Arguments[0].Expression, model, TransformerParameter, profileName, diagnostics, ct);
        if (lambdaBody is null)
        {
            return false;
        }

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
        List<(string destProp, string? lambdaBody, bool isIgnore, ConverterReference? converter, MapFromType? mapFrom)> forMemberConfigs,
        ImmutableArray<string> includedMembers,
        string? whenCondition,
        ConverterReference? wholeObjectConverter,
        string? wholeObjectLambdaBody,
        string? beforeMapBody,
        string? afterMapBody,
        ImmutableArray<ValueTransformerDescriptor> transformers,
        string profileName,
        LocationInfo? origin,
        bool reportUnmapped,
        PropertyTypeCheck.Origin checkOrigin,
        SemanticModel model,
        CancellationToken ct)
    {
        // IM0010: generated methods cannot reference private/protected nested types.
        string? methodAccessibility = SymbolHelpers.GetGeneratedMethodAccessibility(
            sourceSymbol, destSymbol, out var inaccessibleType);
        if (methodAccessibility is null)
        {
            return MappingAnalyzer.CreateInaccessibleTypeDescriptor(sourceSymbol, destSymbol, inaccessibleType!, origin);
        }

        string sourceFullName = SymbolHelpers.GetFullyQualifiedName(sourceSymbol);
        string destFullName = SymbolHelpers.GetFullyQualifiedName(destSymbol);

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // IM0008: destination type is abstract or an interface.
        if (destSymbol.IsAbstract || destSymbol.TypeKind == TypeKind.Interface)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AbstractDestinationType,
                destSymbol.Name).WithLocation(origin));
            return new MappingDescriptor(
                sourceTypeName: sourceSymbol.Name,
                sourceNamespace: SymbolHelpers.GetNamespace(sourceSymbol),
                destTypeName: destSymbol.Name,
                destNamespace: SymbolHelpers.GetNamespace(destSymbol),
                propertyMappings: ImmutableArray<PropertyMappingDescriptor>.Empty,
                diagnostics: diagnostics.ToImmutable(),
                hasCustomConverter: false,
                whenConditionBody: null,
                sourceFullName: sourceFullName,
                destFullName: destFullName,
                methodAccessibility: methodAccessibility);
        }

        var sourceProps = SymbolHelpers.GetPublicReadableProperties(sourceSymbol);
        var destProps = SymbolHelpers.GetPublicSettableProperties(destSymbol);

        // Index source props by name (case-insensitive).
        var sourcePropsByName = new Dictionary<string, IPropertySymbol>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in sourceProps)
        {
            sourcePropsByName[p.Name] = p;
        }

        // Index ForMember configs by dest property name.
        var configByDest = new Dictionary<string, (string? lambdaBody, bool isIgnore, ConverterReference? converter, MapFromType? mapFrom)>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var (dp, lb, ign, cvt, mf) in forMemberConfigs)
        {
            configByDest[dp] = (lb, ign, cvt, mf);
        }

        // Build included-member resolution lookup (first-member-wins on name conflicts).
        // Key: dest property name (case-insensitive)
        // Value: (lambdaBody to emit, typeFqn of the nested property for transformer matching)
        var includedResolution = new Dictionary<string, (string lambdaBody, string typeFqn, IPropertySymbol property, string display)>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (string memberName in includedMembers)
        {
            if (!sourcePropsByName.TryGetValue(memberName, out var memberPropSym))
            {
                continue;
            }

            if (memberPropSym.Type is not INamedTypeSymbol memberType)
            {
                continue;
            }

            bool parentIsRef = memberPropSym.Type.IsReferenceType;
            var nestedProps = SymbolHelpers.GetPublicReadableProperties(memberType);
            foreach (var nestedProp in nestedProps)
            {
                if (includedResolution.ContainsKey(nestedProp.Name))
                {
                    continue; // first member wins
                }

                string access = $"source.{memberName}.{nestedProp.Name}";
                // Emit a null-guard ternary for reference-type nested members to avoid NRE.
                string lambdaBody = parentIsRef
                    ? $"source.{memberName} != null ? {access} : default!"
                    : access;
                string typeFqn = nestedProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                includedResolution[nestedProp.Name] = (lambdaBody, typeFqn, nestedProp, $"{sourceSymbol.Name}.{memberName}.{nestedProp.Name}");
            }
        }

        var propertyMappings = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
        var converters = new ConverterAnalyzer(model.Compilation, sourceSymbol, destSymbol);
        var wholeObject = wholeObjectConverter is null
            ? null
            : converters.Add(wholeObjectConverter.Value.Type, $"ConvertUsing in MappingProfile '{profileName}'",
                wholeObjectConverter.Value.Location, ct);

        // IM0002 for the properties the generated code assigns. Also with ConvertUsing: it replaces MapToX(source), but the
        // in-place overload (public or the private one for IMapper) still assigns the properties.
        var compilation = model.Compilation;

        // The value of a destination member (a property or a constructor parameter): ForMember of the property of the
        // same name (Ignore, UseConverter, MapFrom), the source property of the same name, IncludeMembers. Returns
        // null when nothing maps it; `ignored` for Ignore(). IM0002 (a value the generated code cannot take) goes to
        // `into` and sets `unusable` to its reason. Without `commit` (constructors being compared) no converter is
        // recorded.
        PropertyMappingDescriptor? MapMember(DestinationMember target, bool isInitOnly, bool isParameter,
            ImmutableArray<DiagnosticInfo>.Builder into, bool commit, out bool ignored, out string? unusable)
        {
            ignored = false;
            unusable = null;
            if (configByDest.TryGetValue(target.DisplayName, out var cfg))
            {
                // Priority 1: explicitly configured via ForMember.
                ignored = cfg.isIgnore;
                var converter = cfg.converter is null || cfg.isIgnore
                    ? null
                    : commit
                        ? converters.Add(cfg.converter.Value.Type, $"UseConverter for '{target.DisplayName}' in MappingProfile '{profileName}'",
                            cfg.converter.Value.Location, ct)
                        : new ConverterUse(string.Empty, isCached: false);
                bool skipped = false;
                if (!cfg.isIgnore && converter is null && cfg.lambdaBody is not null && cfg.mapFrom is { } mapFrom
                    && PropertyTypeCheck.CheckMapFrom(compilation, mapFrom.Type, target, destSymbol, mapFrom.Location ?? origin) is { } wrong)
                {
                    into.Add(wrong);
                    skipped = true;
                    unusable = wrong.MessageArgs[2];
                }

                // UseConverter reads the source property of the same name; a parameter takes its name from the source.
                string sourceName = isParameter && sourcePropsByName.TryGetValue(target.Name, out var named) ? named.Name : target.Name;
                return new PropertyMappingDescriptor(
                    sourcePropertyName: sourceName, // fallback name
                    destPropertyName: target.Name,
                    isIgnored: cfg.isIgnore || skipped,
                    converterType: converter?.FullName,
                    needsNullCheck: false,
                    lambdaBody: cfg.lambdaBody,
                    isInitOnly: isInitOnly,
                    converterIsCached: converter?.IsCached ?? false);
            }
            // Priority 2: direct name-match.
            else if (sourcePropsByName.TryGetValue(target.Name, out var namedProp))
            {
                var collection = SymbolHelpers.DetectCollectionMapping(namedProp.Type, target.Type);
                string typeFqn = namedProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                bool skipped = false;
                var mappingPairs = default(EquatableArray<string>);
                NestedObjectMapping? nested = null;
                string sourceDisplay = $"{sourceSymbol.Name}.{namedProp.Name}";
                if (collection is not null)
                {
                    if (MappingAnalyzer.CheckCollection(compilation, collection, namedProp, target, sourceDisplay, destSymbol,
                            checkOrigin, origin, out mappingPairs) is { } missing)
                    {
                        into.Add(missing);
                    }
                }
                else if ((nested = PropertyTypeCheck.CheckNestedObject(compilation, namedProp.Type, target.Type, sourceDisplay,
                             target, destSymbol, checkOrigin, origin, into)) is not null)
                {
                    skipped = nested.Skipped;
                    mappingPairs = nested.Pairs;
                    if (skipped)
                    {
                        unusable = into[into.Count - 1].MessageArgs[2];
                    }
                }
                else if (PropertyTypeCheck.CheckAssignment(compilation, namedProp.Type, target.Type, sourceDisplay, target,
                             destSymbol, checkOrigin, origin) is { } incompatible)
                {
                    into.Add(incompatible);
                    skipped = true;
                    unusable = incompatible.MessageArgs[2];
                }

                return new PropertyMappingDescriptor(
                    sourcePropertyName: namedProp.Name,
                    destPropertyName: target.Name,
                    isIgnored: skipped,
                    converterType: null,
                    needsNullCheck: namedProp.Type.IsReferenceType,
                    lambdaBody: null,
                    isInitOnly: isInitOnly,
                    sourcePropertyTypeFqn: typeFqn,
                    collection: collection,
                    mappingPairs: mappingPairs,
                    nested: nested is { Skipped: false } ? nested : null);
            }
            // Priority 3: IncludeMembers flattening.
            else if (includedResolution.TryGetValue(target.Name, out var included))
            {
                var incompatible = PropertyTypeCheck.CheckAssignment(compilation, included.property.Type, target.Type,
                    included.display, target, destSymbol, checkOrigin, origin);
                if (incompatible is not null)
                {
                    into.Add(incompatible.Value);
                    unusable = incompatible.Value.MessageArgs[2];
                }

                return new PropertyMappingDescriptor(
                    sourcePropertyName: target.Name,
                    destPropertyName: target.Name,
                    isIgnored: incompatible is not null,
                    converterType: null,
                    needsNullCheck: false,
                    lambdaBody: included.lambdaBody,
                    isInitOnly: isInitOnly,
                    includedMemberTypeFqn: included.typeFqn);
            }

            return null;
        }

        // A destination without an accessible parameterless constructor is created by a constructor (issue #24): a
        // parameter is configured by ForMember of the property of the same name (ignoring case); Ignore() leaves its
        // default value. ConvertUsing creates the object itself: no constructor is needed.
        var construction = wholeObjectConverter is not null || wholeObjectLambdaBody is not null
            ? ConstructorChoice.Initializer
            : ConstructorSelection.Choose(compilation, sourceSymbol, destSymbol,
                (parameter, sameNamed, commit) =>
                {
                    var found = ImmutableArray.CreateBuilder<DiagnosticInfo>();
                    var mapping = MapMember(DestinationMember.ForParameter(parameter, sameNamed), isInitOnly: false,
                        isParameter: true, found, commit, out bool ignored, out string? unusable);
                    if (mapping is null)
                    {
                        return ParameterValue.NoSource();
                    }

                    if (ignored)
                    {
                        return ParameterValue.Ignored($"Ignore() of '{sameNamed?.Name ?? parameter.Name}' in MappingProfile '{profileName}'");
                    }

                    return unusable is null ? ParameterValue.Mapped(mapping, found.ToImmutable()) : ParameterValue.Unusable(unusable);
                },
                origin, atParameter: false,
                (parameter, sameNamed) => checkOrigin == PropertyTypeCheck.Origin.ReverseMap
                    ? $"The reverse mapping of ReverseMap() maps by name only: declare it with CreateMap<{sourceSymbol.Name}, {destSymbol.Name}>() and configure '{sameNamed?.Name ?? parameter.Name}' there, or remove ReverseMap()."
                    : sameNamed is not null
                        ? $"Configure it with ForMember(d => d.{sameNamed.Name}, o => o.MapFrom(...)), or add the property to '{sourceSymbol.Name}'."
                        : $"'{destSymbol.Name}' has no property named '{parameter.Name}', so ForMember cannot configure the parameter: add the property to '{sourceSymbol.Name}', or create the object with ConvertUsing(s => new {destSymbol.Name}(...)).");
        diagnostics.AddRange(construction.Diagnostics);

        // No constructor can be called (IM0009, IM0017, IM0018): the mapping is not generated.
        if (construction.Failed)
        {
            return MappingAnalyzer.CreateDiagnosticOnlyDescriptor(diagnostics.ToImmutable());
        }

        foreach (var destProp in destProps)
        {
            ct.ThrowIfCancellationRequested();

            // Set by the constructor (a parameter of the same name, ignoring case).
            if (construction.SetByConstructor.Contains(destProp.Name))
            {
                continue;
            }

            bool isInitOnly = destProp.SetMethod?.IsInitOnly ?? false;

            if (MapMember(DestinationMember.ForProperty(destProp), isInitOnly, isParameter: false, diagnostics, commit: true, out _, out _) is { } mapping)
            {
                propertyMappings.Add(mapping);
            }
            // Priority 4: nothing maps the property. IM0001 at the CreateMap call; reported only with
            // <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles> (filtered in
            // IronMapperGenerator.CollectDiagnostics, so changing the property does not re-run this analysis).
            // [Ignore] on the destination property counts as mapped, as in attribute mappings.
            else if (reportUnmapped && !MappingAnalyzer.HasIgnoreAttribute(destProp))
            {
                diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.UnmappedDestinationProperty,
                        destProp.Name,
                        destSymbol.Name,
                        sourceSymbol.Name)
                    .WithLocation(origin)
                    .WithProperty(MappingAnalyzer.PropertyNameKey, destProp.Name));
            }
        }

        // A converter type that did not resolve: the compiler reports it at UseConverter/ConvertUsing; skip the mapping.
        if (converters.HasUnresolvedType)
        {
            return null;
        }

        // A converter that the generated code cannot create (IM0015): the mapping is not generated.
        if (!converters.CanGenerate)
        {
            return MappingAnalyzer.CreateDiagnosticOnlyDescriptor(ImmutableArray.CreateRange(converters.Diagnostics));
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
            wholeObjectConverterType: wholeObject?.FullName,
            wholeObjectLambdaBody: wholeObjectLambdaBody,
            beforeMapLambdaBody: beforeMapBody,
            afterMapLambdaBody: afterMapBody,
            valueTransformers: transformers,
            sourceFullName: sourceFullName,
            destFullName: destFullName,
            methodAccessibility: methodAccessibility,
            wholeObjectConverterIsCached: wholeObject?.IsCached ?? false,
            usesConstructor: construction.UsesConstructor,
            constructorArguments: construction.Arguments);
    }

    // -----------------------------------------------------------------------
    // Member name extraction
    // -----------------------------------------------------------------------

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
        if (body is CastExpressionSyntax cast)
        {
            body = cast.Expression;
        }

        if (body is ParenthesizedExpressionSyntax paren2)
        {
            body = paren2.Expression;
        }

        return (body as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
    }

    // -----------------------------------------------------------------------
    // Type resolution
    // -----------------------------------------------------------------------

    private static (INamedTypeSymbol? source, INamedTypeSymbol? dest) ExtractCreateMapTypes(
        InvocationExpressionSyntax inv,
        SemanticModel model,
        out bool unresolved,
        CancellationToken ct)
    {
        unresolved = false;
        // CreateMap<TSource, TDest>() — type args on the generic name.
        if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol methodSymbol)
        {
            return (null, null);
        }

        if (methodSymbol.TypeArguments.Length < 2)
        {
            return (null, null);
        }
        // CreateMap<Source, Missing>(): the compiler reports it at the call; skip the mapping.
        if (methodSymbol.TypeArguments[0] is not INamedTypeSymbol source || methodSymbol.TypeArguments[1] is not INamedTypeSymbol dest)
        {
            return (null, null);
        }

        if (SymbolHelpers.ContainsErrorType(source) || SymbolHelpers.ContainsErrorType(dest))
        {
            unresolved = true;
            return (null, null);
        }

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
            if (current.ToDisplayString() == baseTypeFqn)
            {
                return true;
            }

            current = current.BaseType;
        }
        return false;
    }
}
