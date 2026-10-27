using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using IronMapper.Generator.Diagnostics;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Builds the <see cref="MappingPlan"/> from all mappings of the compilation.
/// </summary>
/// <remarks>
/// Rules (all deterministic, independent of file and declaration order where possible):
/// <list type="number">
///   <item>Duplicate type pair → one definition is generated (lowest <see cref="MappingDescriptor.OriginRank"/>:
///   <c>CreateMap</c>, <c>ReverseMap()</c>, <c>[MapTo]</c>, <c>[MapFrom]</c>; then the ordinal order of
///   <see cref="MappingDescriptor.Origin"/>, i.e. profile/type full name and call position), IM0007.</item>
///   <item>Method names: <c>MapTo{Name}</c>, unchanged unless two mappings from the same source type share it
///   (CS0111 in 1.1.2). Then every mapping of that group gets the first candidate that makes all names of the group
///   unique and unused: the last 1, 2, … namespace/containing-type segments of the destination before the name
///   (<c>MapToAdminUserDto</c>), then the type arguments by simple names (<c>MapToBoxOfInt32</c>), then both,
///   then the type arguments by full names. IM0013 for each changed name; no candidate → IM0014, not generated.</item>
///   <item>Helper scope (hooks, transformers): <c>_{Source}_{Dest}</c> by simple names, unless another generated
///   mapping has the same pair of simple names; then the full names are used.</item>
///   <item>Hint name: full names of both types, unique.</item>
///   <item><c>…Core</c> (the body of <c>MapToX(source)</c> without the null check, called by nested objects and
///   collection elements): <c>{MethodName}Core</c>, or <c>{MethodName}Core{HelperScope}</c> when a mapping of the same
///   source type is named so (a destination type <c>XCore</c>; the same parameter would be CS0111).</item>
/// </list>
/// </remarks>
internal static class MappingPlanner
{
    // Qualification modes, tried in this order: namespace/containing types only, + type arguments (simple), + type arguments (full).
    private const int ModeQualifier = 0;
    private const int ModeShortArgs = 1;
    private const int ModeFullArgs = 2;

    public static MappingPlan Create(params ImmutableArray<MappingDescriptor>[] groups)
    {
        var mappings = groups
            .SelectMany(g => g)
            .Where(d => d.SourceTypeName.Length > 0)
            .OrderBy(d => d.PairKey, StringComparer.Ordinal)
            .ThenBy(d => d.OriginRank)
            .ThenBy(d => d.Origin, StringComparer.Ordinal)
            .ToList();

        var diagnostics = new List<DiagnosticInfo>();
        var dropped = new HashSet<string>(StringComparer.Ordinal);

        // 1. Duplicate type pairs: the first of each pair (by rank, then origin) wins.
        var winners = new List<MappingDescriptor>();
        foreach (var pair in mappings.GroupBy(d => d.PairKey, StringComparer.Ordinal))
        {
            var definitions = pair.ToList();
            var winner = definitions[0];
            winners.Add(winner);
            if (definitions.Count == 1)
            {
                continue;
            }

            for (int i = 1; i < definitions.Count; i++)
            {
                dropped.Add(definitions[i].Origin);
            }
            // Points to the first definition that is not generated (the one to remove); the others are related.
            var related = definitions.Skip(2).Append(winner)
                .Where(d => d.OriginLocation is not null)
                .Select(d => d.OriginLocation!.Value);
            diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DuplicateMappingDefinition,
                    Display(winner.SourceFullName),
                    Display(winner.DestFullName),
                    string.Join(", ", definitions.Select(d => d.OriginDisplay)),
                    winner.OriginDisplay)
                .WithLocation(definitions[1].OriginLocation)
                .WithAdditionalLocations(related));
        }

        // 2. Method names, per source type.
        var methodNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bySource in winners.GroupBy(d => d.SourceFullName, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var byPlainName = bySource
                .GroupBy(PlainName, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var single in byPlainName.Where(g => g.Count() == 1))
            {
                used.Add(single.Key);
                methodNames[single.First().Origin] = single.Key;
            }

            foreach (var collision in byPlainName.Where(g => g.Count() > 1))
            {
                var members = collision.OrderBy(d => d.DestFullName, StringComparer.Ordinal).ToList();
                string[]? names = Qualify(members, used);
                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];
                    if (names is null)
                    {
                        skipped.Add(member.Origin);
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UnresolvableMethodNameCollision,
                            Display(member.SourceFullName),
                            Display(member.DestFullName),
                            member.DestTypeName,
                            Candidate(member, ModeQualifier, 1)).WithLocation(member.OriginLocation));
                        continue;
                    }

                    methodNames[member.Origin] = names[i];
                    used.Add(names[i]);
                    if (names[i] != collision.Key)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.QualifiedMethodName,
                            Display(member.SourceFullName),
                            member.DestTypeName,
                            Display(member.DestFullName),
                            names[i],
                            collision.Key).WithLocation(member.OriginLocation));
                    }
                }
            }
        }

        // 3. Helper scopes and 4. hint names for the generated mappings.
        var generated = winners.Where(d => !skipped.Contains(d.Origin)).ToList();
        var shortPairCounts = generated
            .GroupBy(d => d.SourceTypeName + "\0" + d.DestTypeName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var hintNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namesBySource = generated
            .GroupBy(d => d.SourceFullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key!, g => new HashSet<string>(g.Select(d => methodNames[d.Origin]), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var generatedPairs = new HashSet<string>(generated.Select(d => d.PairKey), StringComparer.Ordinal);

        var entries = new List<MappingPlanEntry>();
        foreach (var mapping in mappings)
        {
            bool emit = !dropped.Contains(mapping.Origin) && !skipped.Contains(mapping.Origin);
            if (!emit)
            {
                entries.Add(new MappingPlanEntry(mapping.Origin, mapping.PairKey, false, PlainName(mapping), string.Empty, string.Empty,
                    mapping.MethodAccessibility, mapping.SourceFullName!, mapping.DestFullName!));
                continue;
            }

            string helperScope = shortPairCounts[mapping.SourceTypeName + "\0" + mapping.DestTypeName] == 1
                ? $"_{mapping.SourceTypeName}_{mapping.DestTypeName}"
                : $"_{SymbolHelpers.ToIdentifier(mapping.SourceFullName!)}_{SymbolHelpers.ToIdentifier(mapping.DestFullName!)}";

            string hintBase = $"{ToHintPart(mapping.SourceFullName!)}_MapTo_{ToHintPart(mapping.DestFullName!)}";
            string hintName = hintBase + ".g.cs";
            for (int n = 2; !hintNames.Add(hintName); n++)
            {
                hintName = $"{hintBase}_{n}.g.cs";
            }

            string methodName = methodNames[mapping.Origin];
            string coreName = namesBySource[mapping.SourceFullName!].Contains(methodName + "Core")
                ? methodName + "Core" + helperScope
                : methodName + "Core";
            // A destination created by a constructor has no in-place overload (item 2.15 of the plan): only the private
            // MapInto for the IMapper registration, which throws.
            var overrides = mapping.UsesConstructor
                ? new GeneratedMethodOverrides(inPlace: false, mapping.MethodOverrides.List, mapping.MethodOverrides.Array)
                : mapping.MethodOverrides;
            entries.Add(new MappingPlanEntry(mapping.Origin, mapping.PairKey, true, methodName, hintName, helperScope,
                mapping.MethodAccessibility, mapping.SourceFullName!, mapping.DestFullName!, mapping.UsesConverterCache,
                overrides, coreName, UsesNestedObjectCheck(mapping, generatedPairs), mapping.CanUpdateInPlace));
        }

        return new MappingPlan(
            entries.OrderBy(e => e.Origin, StringComparer.Ordinal).ToImmutableArray(),
            diagnostics.ToImmutableArray());
    }

    private static string PlainName(MappingDescriptor descriptor) => $"MapTo{descriptor.DestTypeName}";

    /// <summary>
    /// The generated code of <paramref name="mapping"/> checks a nested object that the destination does not accept as
    /// <see langword="null"/> (<see cref="NullHandling.Throw"/>) and so calls <c>NestedObjectIsNull&lt;T&gt;</c>, declared
    /// once in <c>GeneratedMappers.g.cs</c>; a property without a generated mapping for its pair (IM0002) is not generated.
    /// </summary>
    private static bool UsesNestedObjectCheck(MappingDescriptor mapping, HashSet<string> generatedPairs)
    {
        foreach (var property in mapping.AllValueMappings)
        {
            if (property.NullHandling != NullHandling.Throw || !MapperCodeEmitter.IsMappedNestedObject(property))
            {
                continue;
            }

            if (property.MappingPairs.Length == 0 || property.MappingPairs.Any(generatedPairs.Contains))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns unique names for <paramref name="members"/> (same source, same plain name) that are not in
    /// <paramref name="used"/>, using the same candidate level for every member; <see langword="null"/> if none works.
    /// </summary>
    private static string[]? Qualify(List<MappingDescriptor> members, HashSet<string> used)
    {
        int maxDepth = Math.Max(1, members.Max(m => Segments(m).Length));
        foreach (int mode in new[] { ModeQualifier, ModeShortArgs, ModeFullArgs })
        {
            for (int depth = 0; depth <= maxDepth; depth++)
            {
                if (mode == ModeQualifier && depth == 0)
                {
                    continue; // the plain name, which collides
                }

                string[] names = members.Select(m => Candidate(m, mode, depth)).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() == names.Length && !names.Any(used.Contains))
                {
                    return names;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <c>MapTo</c> + the last <paramref name="depth"/> namespace/containing-type segments (<c>Global</c> for the
    /// global namespace) + the type name + the type arguments for <see cref="ModeShortArgs"/>/<see cref="ModeFullArgs"/>.
    /// </summary>
    private static string Candidate(MappingDescriptor member, int mode, int depth)
    {
        string[] segments = Segments(member);
        string qualifier = depth == 0
            ? string.Empty
            : segments.Length == 0
                ? "Global"
                : string.Concat(segments.Skip(segments.Length - Math.Min(depth, segments.Length)));
        string typeArguments = mode switch
        {
            ModeShortArgs => member.DestTypeArgsShort,
            ModeFullArgs => member.DestTypeArgsFull,
            _ => string.Empty,
        };
        return $"MapTo{qualifier}{member.DestTypeName}{typeArguments}";
    }

    private static string[] Segments(MappingDescriptor member)
        => member.DestQualifier.Length == 0 ? Array.Empty<string>() : member.DestQualifier.Split('.');

    private static string Display(string? fullName)
        => fullName is null ? string.Empty
            : fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;

    // App.Box<App.Payload> → App.Box_App.Payload_ (hint names allow letters, digits, '.', '_').
    private static string ToHintPart(string fullName)
    {
        char[] chars = Display(fullName).ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '_')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
