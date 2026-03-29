using IronMapper.Generator.Analysis;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IronMapper.Generator;

/// <summary>
/// Roslyn Incremental Source Generator that produces compile-time mapping extension methods
/// from <c>[MapTo]</c> / <c>[MapFrom]</c> attribute annotations.
///
/// For every annotated type pair the generator emits a static extension method such as:
/// <code>
///   public static DestType MapToDestType(this SourceType source) { ... }
/// </code>
/// in the <c>IronMapper.Generated</c> namespace.
/// </summary>
[Generator]
public sealed class IronMapperGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // [MapTo(typeof(TDest))] on source class → extract descriptors (one per attribute).
        var fromMapTo = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "IronMapper.Attributes.MapToAttribute",
                predicate: static (node, _) => IsEligibleTypeDeclaration(node),
                transform: static (ctx, ct) => MappingAnalyzer.ExtractFromMapTo(ctx, ct))
            .SelectMany(static (arr, _) => arr);

        // [MapFrom(typeof(TSource))] on dest class → extract descriptors (one per attribute).
        var fromMapFrom = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "IronMapper.Attributes.MapFromAttribute",
                predicate: static (node, _) => IsEligibleTypeDeclaration(node),
                transform: static (ctx, ct) => MappingAnalyzer.ExtractFromMapFrom(ctx, ct))
            .SelectMany(static (arr, _) => arr);

        context.RegisterSourceOutput(fromMapTo, static (spc, descriptor) =>
            EmitDescriptor(spc, descriptor));

        context.RegisterSourceOutput(fromMapFrom, static (spc, descriptor) =>
            EmitDescriptor(spc, descriptor));
    }

    /// <summary>
    /// Returns true for class/record/struct declarations that are NOT file-scoped.
    /// File-scoped types cannot be referenced from generated code in other files.
    /// </summary>
    private static bool IsEligibleTypeDeclaration(SyntaxNode node)
    {
        SyntaxTokenList modifiers = node switch
        {
            ClassDeclarationSyntax cd => cd.Modifiers,
            RecordDeclarationSyntax rd => rd.Modifiers,
            StructDeclarationSyntax sd => sd.Modifiers,
            _ => default
        };

        if (modifiers == default) return false;

        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
                return false;
        }

        return true;
    }

    private static void EmitDescriptor(SourceProductionContext spc, MappingDescriptor descriptor)
    {
        // Report any diagnostics collected during analysis.
        foreach (var diag in descriptor.Diagnostics)
        {
            spc.ReportDiagnostic(
                Diagnostic.Create(diag.Descriptor, Location.None, diag.MessageArgs));
        }

        var (hintName, source) = MapperCodeEmitter.Emit(descriptor);
        spc.AddSource(hintName, source);
    }
}
