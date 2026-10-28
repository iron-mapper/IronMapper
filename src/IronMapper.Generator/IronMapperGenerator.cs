using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using IronMapper.Generator.Analysis;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using IronMapper.Generator.Diagnostics;
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

        // MSBuild properties (build_property.*). Each output takes only the part it uses, so changing one property
        // re-emits only what depends on it: the overloads (IronMapperGenerate*) and IronMapperRegistration the plan, the
        // mapping files and the registration, IronMapperExcludeFromCodeCoverage and IronMapperGeneratedCodeAttribute the
        // class declaration, IronMapperReportUnmappedInProfiles the diagnostics.
        var settings = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => GeneratorSettings.From(options.GlobalOptions))
            .WithTrackingName(TrackingNames.Settings);
        var methods = settings.Select(static (value, _) => value.Methods)
            .WithTrackingName(TrackingNames.MethodSettings);
        var registration = settings.Select(static (value, _) => value.Registration)
            .WithTrackingName(TrackingNames.RegistrationSettings);

        // The overloads and the registration take part in the plan: an in-place body that updates nested objects (and so
        // needs their in-place …Core) exists only when the overload or the registration is generated.
        var plan = all.Combine(methods.Combine(registration))
            .Select(static (item, _) => MappingPlanner.Create(item.Right.Left, item.Right.Right,
                item.Left.Left.Left, item.Left.Left.Right, item.Left.Right))
            .WithTrackingName(TrackingNames.Plan);

        // The generated code does not depend on diagnostics, and diagnostics carry locations, which change whenever a
        // line is added above a mapping. Sources are generated from copies without them (compared by value, so an edit
        // that only moves a mapping leaves every source output cached); diagnostics have their own output below.
        var sourcePlan = plan.Select(static (mappingPlan, _) => mappingPlan.WithoutDiagnostics())
            .WithTrackingName(TrackingNames.SourcePlan);

        // APIs the generated code may use in this compilation (ThrowIfNull, DoesNotReturn). Recomputed for every
        // compilation, compared by value: the outputs change only when the target framework does. With them
        // IronMapperNullSourceReturnsNull (the mapping files, the helpers of the class declaration) and
        // IronMapperEmitPolyfills (the attribute copies in the class declaration and the registration).
        var compilationOptions = context.CompilationProvider
            .Select(static (compilation, _) => GeneratedCodeOptions.From(compilation))
            .WithTrackingName(TrackingNames.CompilationOptions);
        var codeSettings = settings.Select(static (value, _) => (value.NullSourceReturnsNull, value.EmitPolyfills))
            .WithTrackingName(TrackingNames.CodeSettings);
        var codeOptions = compilationOptions.Combine(codeSettings)
            .Select(static (item, _) => item.Left.WithSettings(item.Right.NullSourceReturnsNull, item.Right.EmitPolyfills))
            .WithTrackingName(TrackingNames.CodeOptions);

        // Attributes of the class declaration: [ExcludeFromCodeCoverage] (IronMapperExcludeFromCodeCoverage) and
        // [GeneratedCode] (IronMapperGeneratedCodeAttribute). A value tuple: compared by value.
        var classAttributes = settings
            .Select(static (value, _) => (ExcludeFromCodeCoverage: value.ExcludeFromCodeCoverage,
                GeneratedCode: value.GeneratedCodeAttribute))
            .WithTrackingName(TrackingNames.CoverageSettings);

        var planAndOptions = sourcePlan.Combine(codeOptions.Combine(methods.Combine(registration)));

        context.RegisterSourceOutput(WithoutDiagnostics(fromMapTo, TrackingNames.MapToSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right.Left,
                item.Right.Right.Right.Right));

        context.RegisterSourceOutput(WithoutDiagnostics(fromMapFrom, TrackingNames.MapFromSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right.Left,
                item.Right.Right.Right.Right));

        context.RegisterSourceOutput(WithoutDiagnostics(fromProfiles, TrackingNames.ProfilesSource).Combine(planAndOptions),
            static (spc, item) => EmitDescriptor(spc, item.Left, item.Right.Left, item.Right.Right.Left, item.Right.Right.Right.Left,
                item.Right.Right.Right.Right));

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
            if (found.Length == 0)
            {
                return;
            }

            var trees = LocationInfo.TreesByPath(compilation);
            var options = compilation.Options.SpecificDiagnosticOptions;
            foreach (var diagnostic in found)
            {
                // IM0023 replaces a compiler warning inside the generated file and takes its configuration for every file
                // (NoWarn/WarningsAsErrors, a .globalconfig; an .editorconfig section does not reach the generated files):
                // suppressed, it is not reported (a clean build stays clean, also with TreatWarningsAsErrors); an error
                // (WarningsAsErrors=nullable), it is reported as an error (a failing build keeps failing).
                DiagnosticSeverity? severity = null;
                if (diagnostic.GetProperty(PropertyTypeCheck.ReplacesWarningKey) is { } replaced)
                {
                    var report = options.TryGetValue(replaced, out var specific) ? specific
                        : compilation.Options.SyntaxTreeOptionsProvider is { } provider
                          && provider.TryGetGlobalDiagnosticValue(replaced, default, out var global) ? global
                        : ReportDiagnostic.Default;
                    if (report == ReportDiagnostic.Suppress)
                    {
                        continue;
                    }

                    if (report == ReportDiagnostic.Error)
                    {
                        severity = DiagnosticSeverity.Error;
                    }
                }

                spc.ReportDiagnostic(diagnostic.ToDiagnostic(location => location.ToLocation(trees), severity));
            }
        });

        // Accessibility of the generated class over all generated mappings of the assembly: public if any generated
        // method is public, internal otherwise (an internal class is invisible to referencing assemblies). Mappings
        // the plan does not generate (IM0007/IM0014) do not count. The same declaration holds the converter cache, the
        // throw helper of nested objects and the generic loops of MapToXList/MapToXArray when a generated mapping uses them. The final Select yields a value
        // tuple, so the declaration is re-emitted only when one of these actually changes.
        var classAccessibility = sourcePlan.Combine(methods).Select(static (item, _) =>
                (Accessibility: GetClassAccessibility(item.Left), UsesConverterCache: UsesConverterCache(item.Left),
                    UsesNestedObjectCheck: UsesNestedObjectCheck(item.Left),
                    UsesList: UsesOverload(item.Left, item.Right, static m => m.List),
                    UsesArray: UsesOverload(item.Left, item.Right, static m => m.Array)))
            .WithTrackingName(TrackingNames.ClassAccessibility);

        context.RegisterSourceOutput(classAccessibility.Combine(codeOptions.Combine(classAttributes)), static (spc, item) =>
        {
            // No generated mappings: there is no class to declare, only the namespace (for the global using).
            var ((accessibility, usesConverterCache, usesNestedObjectCheck, usesList, usesArray), (options, (exclude, generatedCode))) = item;
            var (hintName, source) = accessibility is null
                ? MapperCodeEmitter.EmitEmptyNamespace()
                : MapperCodeEmitter.EmitClassDeclaration(accessibility, usesConverterCache, exclude, usesList, usesArray, options,
                    generatedCode, usesNestedObjectCheck);
            spc.AddSource(hintName, source);
        });

        // Registration of the generated mappings for IMapper (module initializer → MapperRegistry). Re-emitted only
        // when the set of generated mappings, their method names, their overloads or the options change. None with
        // IronMapperRegistration=false (item 2.16 of the plan).
        var registrations = sourcePlan.Combine(methods.Combine(registration))
            .Select(static (item, _) => item.Right.Right
                ? GetRegistrations(item.Left, item.Right.Left)
                : EquatableArray<MappingRegistration>.Empty)
            .WithTrackingName(TrackingNames.Registrations);

        context.RegisterSourceOutput(registrations.Combine(codeOptions), static (spc, item) =>
        {
            var (mappings, options) = item;
            if (mappings.Count == 0)
            {
                return;
            }

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
    /// last message argument: the plan does not depend on the assembly name. The IM0004 candidates of automatically
    /// mapped nested objects are reported only when the property closes a cycle of generated mappings
    /// (<see cref="MappingCycles"/>), with the chain as the last message argument.
    /// </summary>
    private static EquatableArray<DiagnosticInfo> CollectDiagnostics(ImmutableArray<MappingDescriptor> fromMapTo,
        ImmutableArray<MappingDescriptor> fromMapFrom, ImmutableArray<MappingDescriptor> fromProfiles, MappingPlan plan,
        GeneratorSettings settings, string className)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        MappingCycles? cycles = null;
        Add(fromMapTo, isProfile: false);
        Add(fromMapFrom, isProfile: false);
        Add(fromProfiles, isProfile: true);
        foreach (var diagnostic in plan.Diagnostics)
        {
            builder.Add(diagnostic.Descriptor == DiagnosticDescriptors.QualifiedMethodName
                ? diagnostic.WithMessageArg(className)
                : diagnostic);
        }

        return builder.ToImmutable();

        void Add(ImmutableArray<MappingDescriptor> descriptors, bool isProfile)
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.SourceTypeName.Length != 0 && plan.Find(descriptor.Origin) is not { Emit: true })
                {
                    continue;
                }

                foreach (var diagnostic in descriptor.Diagnostics)
                {
                    if (isProfile && !settings.ReportUnmappedInProfiles
                                  && diagnostic.Descriptor == DiagnosticDescriptors.UnmappedDestinationProperty)
                    {
                        continue;
                    }
                    // IM0002 of a collection element or a nested object: only when no mapping of the pair is generated (the
                    // same condition skips the property in the generated code, MapperCodeEmitter.IsMappingMissing).
                    if (diagnostic.GetProperty(PropertyTypeCheck.MissingElementPairsKey) is { } pairs
                        && plan.GeneratesAny(pairs.Split('|')))
                    {
                        continue;
                    }
                    // IM0023 of nullable collection elements: only when their mapping is generated (otherwise IM0002 says why).
                    if (diagnostic.GetProperty(PropertyTypeCheck.IfMappingForKey) is { } mapped
                        && !plan.GeneratesAny(mapped.Split('|')))
                    {
                        continue;
                    }

                    if (diagnostic.GetProperty(PropertyTypeCheck.CyclePropertyKey) is { } property)
                    {
                        cycles ??= new MappingCycles(Generated(), plan);
                        if (cycles.FindCycle(descriptor, property) is { } chain)
                        {
                            builder.Add(diagnostic.WithMessageArg(chain));
                        }

                        continue;
                    }
                    builder.Add(diagnostic);
                }

                if (descriptor.SourceTypeName.Length != 0 && InPlaceSkipsInitProperties(descriptor, isProfile) is { } skipped)
                {
                    builder.Add(skipped);
                }
            }
        }

        // IM0025: the in-place overload is generated (the assembly setting with the mapping's override) and leaves mapped
        // init-only properties as they are. Not for one that changes nothing ([Obsolete], InPlaceIsNoOp) or a destination
        // created by a constructor or a factory (no in-place overload).
        DiagnosticInfo? InPlaceSkipsInitProperties(MappingDescriptor descriptor, bool isProfile)
        {
            var entry = plan.Find(descriptor.Origin)!;
            if (descriptor.UsesConstructor || entry.InPlaceIsNoOp || !settings.Methods.With(entry.MethodOverrides).InPlace)
            {
                return null;
            }

            var skipped = new List<string>();
            var whenNotNull = new List<string>();
            MapperCodeEmitter.InPlaceSkippedProperties(descriptor, plan, skipped, whenNotNull);
            if (skipped.Count == 0 && whenNotNull.Count == 0)
            {
                return null;
            }

            var what = new List<string>();
            if (skipped.Count > 0)
            {
                what.Add(skipped.Count == 1
                    ? $"{skipped[0]} is init-only, so an existing object keeps its value"
                    : $"{Join(skipped)} are init-only, so an existing object keeps their values");
            }

            if (whenNotNull.Count > 0)
            {
                what.Add(whenNotNull.Count == 1
                    ? $"{whenNotNull[0]} is an init-only nested object: an existing one is updated in place, but a null one stays null and a null source value is not assigned"
                    : $"{Join(whenNotNull)} are init-only nested objects: an existing one is updated in place, but a null one stays null and a null source value is not assigned");
            }

            string first = skipped.Count > 0 ? skipped[0] : whenNotNull[0];
            string hint = !isProfile
                ? "If they need not be mapped, mark them [Ignore] (a new object then does not get them either); if the in-place update is not needed, turn it off with GenerateInPlace = false"
                : descriptor.OriginRank == 1
                    ? $"The reverse mapping of ReverseMap() takes no ForMember: declare it with CreateMap<{descriptor.SourceTypeName}, {descriptor.DestTypeName}>() to configure it, or turn the in-place overloads off with IronMapperGenerateInPlace=false"
                    : $"If they need not be mapped, use ForMember(d => d.{first}, o => o.Ignore()) for each (a new object then does not get them either); if the in-place update is not needed, use GenerateMethods(inPlace: false)";
            return new DiagnosticInfo(DiagnosticDescriptors.InPlaceSkipsInitProperties, descriptor.SourceFullName!.Replace("global::", string.Empty),
                    descriptor.DestFullName!.Replace("global::", string.Empty), string.Join("; ", what), hint)
                .WithLocation(descriptor.OriginLocation);
        }

        static string Join(List<string> names)
            => names.Count == 2 ? $"{names[0]} and {names[1]}" : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        IEnumerable<MappingDescriptor> Generated()
            => fromMapTo.Concat(fromMapFrom).Concat(fromProfiles)
                .Where(d => d.SourceTypeName.Length != 0 && plan.Find(d.Origin) is { Emit: true });
    }

    private static EquatableArray<MappingRegistration> GetRegistrations(MappingPlan plan, GeneratedMethods methods)
    {
        var builder = ImmutableArray.CreateBuilder<MappingRegistration>();
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit)
            {
                // An in-place overload that changes nothing is [Obsolete]: the registration calls the private MapInto,
                // which throws (MapperCodeEmitter.Emit).
                var generated = methods.With(entry.MethodOverrides);
                if (entry.InPlaceIsNoOp && generated.InPlace)
                {
                    generated = new GeneratedMethods(inPlace: false, generated.List, generated.Array);
                }

                builder.Add(new MappingRegistration(entry.SourceFullName, entry.DestFullName, entry.MethodName,
                    entry.HelperScope, generated));
            }
        }

        return builder.ToImmutable();
    }

    private static string? GetClassAccessibility(MappingPlan plan)
    {
        string? result = null;
        foreach (var entry in plan.Entries)
        {
            if (!entry.Emit)
            {
                continue;
            }

            if (entry.MethodAccessibility == "public")
            {
                return "public";
            }

            result = "internal";
        }

        return result;
    }

    /// <summary>A generated mapping has the overload chosen by <paramref name="overload"/> (with its overrides applied).</summary>
    private static bool UsesOverload(MappingPlan plan, GeneratedMethods methods, Func<GeneratedMethods, bool> overload)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit && overload(methods.With(entry.MethodOverrides)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesConverterCache(MappingPlan plan)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit && entry.UsesConverterCache)
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesNestedObjectCheck(MappingPlan plan)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.Emit && entry.UsesNestedObjectCheck)
            {
                return true;
            }
        }

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

        if (modifiers == default)
        {
            return false;
        }

        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the generated source file of <paramref name="descriptor"/> to the compilation. Diagnostic-only descriptors
    /// (empty SourceTypeName) and definitions that the plan does not generate (duplicate pair, unresolvable name
    /// collision) produce no source. Diagnostics are reported by their own output (see <see cref="CollectDiagnostics"/>).
    /// </summary>
    private static void EmitDescriptor(SourceProductionContext spc, MappingDescriptor descriptor, MappingPlan plan,
        GeneratedCodeOptions options, GeneratedMethods methods, bool registration)
    {
        if (descriptor.SourceTypeName.Length == 0 || plan.Find(descriptor.Origin) is not { Emit: true } entry)
        {
            return;
        }

        spc.AddSource(entry.HintName,
            MapperCodeEmitter.Emit(descriptor, entry, plan, options, methods.With(entry.MethodOverrides), registration));
    }
}
