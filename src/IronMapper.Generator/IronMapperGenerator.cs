using IronMapper.Generator.Analysis;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

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
    /// <summary>
    /// Registers the incremental pipeline.  Called once by the Roslyn host on startup.
    /// Two parallel pipelines handle <c>[MapTo]</c> and <c>[MapFrom]</c> independently
    /// so that each trigger type benefits from fine-grained caching.
    /// </summary>
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

        // MappingProfile subclass pipeline — analyses constructor bodies for CreateMap<,>() chains.
        var fromProfiles = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: ProfileAnalyzer.IsCandidateClass,
                transform: static (ctx, ct) => ProfileAnalyzer.ExtractFromProfile(ctx, ct))
            .SelectMany(static (arr, _) => arr);

        context.RegisterSourceOutput(fromProfiles, static (spc, descriptor) =>
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

    /// <summary>
    /// Reports any analysis diagnostics collected in <paramref name="descriptor"/> and then
    /// adds the generated source file to the compilation.
    /// Sentinel descriptors (SourceTypeName is empty) carry only diagnostics and produce no source.
    /// </summary>
    private static void EmitDescriptor(SourceProductionContext spc, MappingDescriptor descriptor)
    {
        // Report any diagnostics collected during analysis.
        foreach (var diag in descriptor.Diagnostics)
        {
            spc.ReportDiagnostic(
                Diagnostic.Create(diag.Descriptor, Location.None, diag.MessageArgs));
        }

        // Sentinel descriptors (e.g. empty MappingProfile) carry diagnostics only.
        if (descriptor.SourceTypeName.Length == 0) return;

        var (hintName, source) = MapperCodeEmitter.Emit(descriptor);
        spc.AddSource(hintName, source);
    }
}
