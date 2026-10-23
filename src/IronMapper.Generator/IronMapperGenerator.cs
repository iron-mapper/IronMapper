using System;
using IronMapper.Generator.Analysis;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using IronMapper.Generator.Diagnostics;
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
        var all = fromMapTo.Collect()
            .Combine(fromMapFrom.Collect())
            .Combine(fromProfiles.Collect());
        var plan = all
            .Select(static (all, _) => MappingPlanner.Create(all.Left.Left, all.Left.Right, all.Right))
            .WithTrackingName(TrackingNames.Plan);

        // The generated code does not depend on diagnostics, and diagnostics carry locations, which change whenever a
        // line is added above a mapping. Sources are generated from copies without them (compared by value, so an edit
        // that only moves a mapping leaves every source output cached); diagnostics have their own output below.
        var sourcePlan = plan.Select(static (mappingPlan, _) => mappingPlan.WithoutDiagnostics())
            .WithTrackingName(TrackingNames.SourcePlan);

        // APIs the generated code may use in this compilation (ThrowIfNull, DoesNotReturn). Recomputed for every
        // compilation, compared by value: the outputs change only when the target framework does.
        var codeOptions = context.CompilationProvider
            .Select(static (compilation, _) => GeneratedCodeOptions.From(compilation))
            .WithTrackingName(TrackingNames.CodeOptions);

        // MSBuild properties (build_property.*). Each output takes only the part it uses, so changing one property
        // re-emits only what depends on it: the overloads (IronMapperGenerate*) the mapping files and the registration,
        // IronMapperExcludeFromCodeCoverage and IronMapperGeneratedCodeAttribute the class declaration,
        // IronMapperReportUnmappedInProfiles the diagnostics.
        var settings = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => GeneratorSettings.From(options.GlobalOptions))
            .WithTrackingName(TrackingNames.Settings);
        var methods = settings.Select(static (value, _) => value.Methods)
            .WithTrackingName(TrackingNames.MethodSettings);
        // Attributes of the class declaration: [ExcludeFromCodeCoverage] (IronMapperExcludeFromCodeCoverage) and
        // [GeneratedCode] (IronMapperGeneratedCodeAttribute). A value tuple: compared by value.
        var classAttributes = settings
            .Select(static (value, _) => (ExcludeFromCodeCoverage: value.ExcludeFromCodeCoverage,
                GeneratedCode: value.GeneratedCodeAttribute))
            .WithTrackingName(TrackingNames.CoverageSettings);

        var planAndOptions = sourcePlan.Combine(codeOptions.Combine(methods));

        context.RegisterSourceOutput(WithoutDiagnostics(fromMapTo, TrackingNames.MapToSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right));

        context.RegisterSourceOutput(WithoutDiagnostics(fromMapFrom, TrackingNames.MapFromSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right));

        context.RegisterSourceOutput(WithoutDiagnostics(fromProfiles, TrackingNames.ProfilesSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right));

        // Diagnostics of all mappings and of the plan, as one value (compared by value: cached until a diagnostic or its
        // location changes). The output resolves the locations in the current compilation's syntax trees: a location
        // in a tree is what the IDE underlines, what .editorconfig severities apply to and what code fixes need. That
        // output therefore runs for every compilation; it only reports, it generates no code.
        // The generated class name (GeneratedMappers_{AssemblyName}) is part of the IM0013 message.
        var className = codeOptions.Select(static (options, _) => options.ClassName)
            .WithTrackingName(TrackingNames.ClassName);
        var diagnostics = all.Combine(plan).Combine(settings.Combine(className))
            .Select(static (item, _) => CollectDiagnostics(
                item.Left.Left.Left.Left, item.Left.Left.Left.Right, item.Left.Left.Right, item.Left.Right, item.Right.Left,
                item.Right.Right))
            .WithTrackingName(TrackingNames.Diagnostics);

        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (spc, item) =>
        {
            var (found, compilation) = item;
            if (found.Length == 0) return;
            var trees = LocationInfo.TreesByPath(compilation);
            foreach (var diagnostic in found)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic(location => location.ToLocation(trees)));
        });

        // Accessibility of the generated class over all generated mappings of the assembly: public if any generated
        // method is public, internal otherwise (an internal class is invisible to referencing assemblies). Mappings
        // the plan does not generate (IM0007/IM0014) do not count. The same declaration holds the converter cache and the
        // generic loops of MapToXList/MapToXArray when a generated mapping uses them. The final Select yields a value
        // tuple, so the declaration is re-emitted only when one of these actually changes.
        var classAccessibility = sourcePlan.Combine(methods).Select(static (item, _) =>
                (Accessibility: GetClassAccessibility(item.Left), UsesConverterCache: UsesConverterCache(item.Left),
                    UsesList: UsesOverload(item.Left, item.Right, static m => m.List),
                    UsesArray: UsesOverload(item.Left, item.Right, static m => m.Array)))
            .WithTrackingName(TrackingNames.ClassAccessibility);

        context.RegisterSourceOutput(classAccessibility.Combine(codeOptions.Combine(classAttributes)), static (spc, item) =>
        {
            // No generated mappings: there is no class to declare, only the namespace (for the global using).
            var ((accessibility, usesConverterCache, usesList, usesArray), (options, (exclude, generatedCode))) = item;
            var (hintName, source) = accessibility is null
                ? MapperCodeEmitter.EmitEmptyNamespace()
                : MapperCodeEmitter.EmitClassDeclaration(accessibility, usesConverterCache, exclude, usesList, usesArray, options,
                    generatedCode);
            spc.AddSource(hintName, source);
        });

        // Registration of the generated mappings for IMapper (module initializer → MapperRegistry). Re-emitted only
        // when the set of generated mappings, their method names, their overloads or the options change.
        var registrations = sourcePlan.Combine(methods)
            .Select(static (item, _) => GetRegistrations(item.Left, item.Right))
            .WithTrackingName(TrackingNames.Registrations);

        context.RegisterSourceOutput(registrations.Combine(codeOptions), static (spc, item) =>
        {
            var (mappings, options) = item;
            if (mappings.Count == 0) return;
            var (hintName, source) = MapperCodeEmitter.EmitRegistration(mappings, options);
            spc.AddSource(hintName, source);
        });
    }

    /// <summary>The descriptors as the code is generated from them: without diagnostics and locations.</summary>
    private static IncrementalValuesProvider<MappingDescriptor> WithoutDiagnostics(
        IncrementalValuesProvider<MappingDescriptor> descriptors, string trackingName)
        => descriptors.Select(static (descriptor, _) => descriptor.WithoutDiagnostics()).WithTrackingName(trackingName);

    /// <summary>
    /// The diagnostics to report: those of every mapping the plan generates (a duplicate definition, IM0007, or a
    /// mapping skipped by IM0014 reports only the plan's diagnostic), of diagnostic-only descriptors (IM0005, IM0010,
    /// IM0015, …) and of the plan. IM0001 of profile mappings only when <see cref="GeneratorSettings.ReportUnmappedInProfiles"/>
    /// is set (off by default). IM0013 gets the name of the generated class (<paramref name="className"/>) as its
    /// last message argument: the plan does not depend on the assembly name.
    /// </summary>
    private static EquatableArray<DiagnosticInfo> CollectDiagnostics(ImmutableArray<MappingDescriptor> fromMapTo,
        ImmutableArray<MappingDescriptor> fromMapFrom, ImmutableArray<MappingDescriptor> fromProfiles, MappingPlan plan,
        GeneratorSettings settings, string className)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        Add(fromMapTo, isProfile: false);
        Add(fromMapFrom, isProfile: false);
        Add(fromProfiles, isProfile: true);
        foreach (var diagnostic in plan.Diagnostics)
            builder.Add(diagnostic.Descriptor == DiagnosticDescriptors.QualifiedMethodName
                ? diagnostic.WithMessageArg(className)
                : diagnostic);
        return builder.ToImmutable();

        void Add(ImmutableArray<MappingDescriptor> descriptors, bool isProfile)
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.SourceTypeName.Length != 0 && plan.Find(descriptor.Origin) is not { Emit: true }) continue;
                foreach (var diagnostic in descriptor.Diagnostics)
                {
                    if (isProfile && !settings.ReportUnmappedInProfiles
                                  && diagnostic.Descriptor == DiagnosticDescriptors.UnmappedDestinationProperty)
                        continue;
                    // IM0002 of a collection element: only when no mapping of the element types is generated (the same
                    // condition skips the property in the generated code, MapperCodeEmitter.IsElementMappingMissing).
                    if (diagnostic.GetProperty(PropertyTypeCheck.MissingElementPairsKey) is { } pairs
                        && plan.GeneratesAny(pairs.Split('|')))
                        continue;
                    builder.Add(diagnostic);
                }
            }
        }
    }

    private static EquatableArray<MappingRegistration> GetRegistrations(MappingPlan plan, GeneratedMethods methods)
    {
        var builder = ImmutableArray.CreateBuilder<MappingRegistration>();
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit)
                builder.Add(new MappingRegistration(entry.SourceFullName, entry.DestFullName, entry.MethodName,
                    entry.HelperScope, methods.With(entry.MethodOverrides)));
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

    /// <summary>A generated mapping has the overload chosen by <paramref name="overload"/> (with its overrides applied).</summary>
    private static bool UsesOverload(MappingPlan plan, GeneratedMethods methods, Func<GeneratedMethods, bool> overload)
    {
        foreach (var entry in plan.Entries)
            if (entry.Emit && overload(methods.With(entry.MethodOverrides))) return true;
        return false;
    }

    private static bool UsesConverterCache(MappingPlan plan)
    {
        foreach (var entry in plan.Entries)
            if (entry.Emit && entry.UsesConverterCache) return true;
        return false;
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
    /// Adds the generated source file of <paramref name="descriptor"/> to the compilation. Diagnostic-only descriptors
    /// (empty SourceTypeName) and definitions that the plan does not generate (duplicate pair, unresolvable name
    /// collision) produce no source. Diagnostics are reported by their own output (see <see cref="CollectDiagnostics"/>).
    /// </summary>
    private static void EmitDescriptor(SourceProductionContext spc, MappingDescriptor descriptor, MappingPlan plan,
        GeneratedCodeOptions options, GeneratedMethods methods)
    {
        if (descriptor.SourceTypeName.Length == 0 || plan.Find(descriptor.Origin) is not { Emit: true } entry) return;

        spc.AddSource(entry.HintName,
            MapperCodeEmitter.Emit(descriptor, entry, plan, options, methods.With(entry.MethodOverrides)));
    }
}
