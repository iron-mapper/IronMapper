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
            .WithTrackingName(TrackingNames.MapToTransform)
            .SelectMany(static (arr, _) => arr.AsImmutableArray())
            .WithTrackingName(TrackingNames.MapTo);

        // [MapFrom(typeof(TSource))] on dest class → extract descriptors (one per attribute).
        var fromMapFrom = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "IronMapper.Attributes.MapFromAttribute",
                predicate: static (node, _) => IsEligibleTypeDeclaration(node),
                transform: static (ctx, ct) => MappingAnalyzer.ExtractFromMapFrom(ctx, ct))
            .WithTrackingName(TrackingNames.MapFromTransform)
            .SelectMany(static (arr, _) => arr.AsImmutableArray())
            .WithTrackingName(TrackingNames.MapFrom);

        // MappingProfile subclass pipeline — analyses constructor bodies for CreateMap<,>() chains.
        var fromProfiles = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: ProfileAnalyzer.IsCandidateClass,
                transform: static (ctx, ct) => ProfileAnalyzer.ExtractFromProfile(ctx, ct))
            .WithTrackingName(TrackingNames.ProfileTransform)
            .SelectMany(static (arr, _) => arr.AsImmutableArray())
            .WithTrackingName(TrackingNames.Profiles);

        // Decisions across all mappings: duplicate pairs (IM0007), method name collisions (IM0013/IM0014),
        // hint names and helper scopes. The plan is compared by value, so a mapping's file is re-emitted only
        // when that mapping or a decision changes.
        var plan = fromMapTo.Collect()
            .Combine(fromMapFrom.Collect())
            .Combine(fromProfiles.Collect())
            .Select(static (all, _) => MappingPlanner.Create(all.Left.Left, all.Left.Right, all.Right))
            .WithTrackingName(TrackingNames.Plan);

        // APIs the generated code may use in this compilation (ThrowIfNull, DoesNotReturn). Recomputed for every
        // compilation, compared by value: the outputs change only when the target framework does.
        var codeOptions = context.CompilationProvider
            .Select(static (compilation, _) => GeneratedCodeOptions.From(compilation))
            .WithTrackingName(TrackingNames.CodeOptions);
        var planAndOptions = plan.Combine(codeOptions);

        context.RegisterSourceOutput(plan, static (spc, mappingPlan) =>
        {
            foreach (var diag in mappingPlan.Diagnostics)
                spc.ReportDiagnostic(diag.ToDiagnostic());
        });

        context.RegisterSourceOutput(fromMapTo.Combine(planAndOptions), static (spc, item) =>
            EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right));

        context.RegisterSourceOutput(fromMapFrom.Combine(planAndOptions), static (spc, item) =>
            EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right));

        context.RegisterSourceOutput(fromProfiles.Combine(planAndOptions), static (spc, item) =>
            EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right));

        // Accessibility of GeneratedMappers over all generated mappings of the assembly: public if any generated
        // method is public, internal otherwise (an internal class is invisible to referencing assemblies). Mappings
        // the plan does not generate (IM0007/IM0014) do not count. The final Select yields a string, so the
        // declaration is re-emitted only when the accessibility actually changes.
        var classAccessibility = plan.Select(static (mappingPlan, _) => GetClassAccessibility(mappingPlan))
            .WithTrackingName(TrackingNames.ClassAccessibility);

        context.RegisterSourceOutput(classAccessibility.Combine(codeOptions), static (spc, item) =>
        {
            // No generated mappings: there is no class to declare, only the namespace (for the global using).
            var (accessibility, options) = item;
            var (hintName, source) = accessibility is null
                ? MapperCodeEmitter.EmitEmptyNamespace()
                : MapperCodeEmitter.EmitClassDeclaration(accessibility, options);
            spc.AddSource(hintName, source);
        });

        // Registration of the generated mappings for IMapper (module initializer → MapperRegistry). Re-emitted only
        // when the set of generated mappings, their method names or the options change.
        var registrations = plan.Select(static (mappingPlan, _) => GetRegistrations(mappingPlan))
            .WithTrackingName(TrackingNames.Registrations);

        context.RegisterSourceOutput(registrations.Combine(codeOptions), static (spc, item) =>
        {
            var (mappings, options) = item;
            if (mappings.Count == 0) return;
            var (hintName, source) = MapperCodeEmitter.EmitRegistration(mappings, options);
            spc.AddSource(hintName, source);
        });
    }

    private static EquatableArray<MappingRegistration> GetRegistrations(MappingPlan plan)
    {
        var builder = ImmutableArray.CreateBuilder<MappingRegistration>();
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit) builder.Add(new MappingRegistration(entry.SourceFullName, entry.DestFullName, entry.MethodName));
        }

        return builder.ToImmutable();
    }

    private static string? GetClassAccessibility(MappingPlan plan)
    {
        string? result = null;
        foreach (var entry in plan.Entries)
        {
            if (!entry.Emit) continue;
            if (entry.MethodAccessibility == "public") return "public";
            result = "internal";
        }

        return result;
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
    /// Definitions that the plan does not generate (duplicate pair, unresolvable name collision) produce
    /// neither source nor their own diagnostics; the plan reports IM0007/IM0014 for them.
    /// </summary>
    private static void EmitDescriptor(SourceProductionContext spc, MappingDescriptor descriptor, MappingPlan plan,
        GeneratedCodeOptions options)
    {
        var entry = descriptor.SourceTypeName.Length == 0 ? null : plan.Find(descriptor.Origin);
        if (descriptor.SourceTypeName.Length != 0 && entry is not { Emit: true }) return;

        // Report any diagnostics collected during analysis.
        foreach (var diag in descriptor.Diagnostics)
            spc.ReportDiagnostic(diag.ToDiagnostic());

        // Sentinel descriptors (e.g. empty MappingProfile) carry diagnostics only.
        if (entry is null) return;

        spc.AddSource(entry.HintName, MapperCodeEmitter.Emit(descriptor, entry, plan, options));
    }
}
