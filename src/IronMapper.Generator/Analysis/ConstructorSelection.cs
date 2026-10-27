using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Analysis;

/// <summary>How a constructor parameter gets its value (<see cref="ParameterValue"/>).</summary>
internal enum ParameterValueKind
{
    /// <summary>A source value (a property of the same name, <c>[MapProperty]</c>, <c>MapFrom</c>, a converter).</summary>
    Mapped,

    /// <summary>Nothing maps it: the default value if the parameter has one, otherwise IM0009.</summary>
    NoSource,

    /// <summary>Ignored (<c>Ignore()</c>, <c>[Ignore]</c>): the default value if the parameter has one, otherwise IM0009.</summary>
    Ignored,

    /// <summary>The source value cannot be passed (the IM0002 reasons of a property): IM0009, even with a default value.</summary>
    Unusable,
}

/// <summary>The value of one constructor parameter, found by the analyzer of the mapping (attributes or a profile).</summary>
internal sealed class ParameterValue
{
    private ParameterValue(ParameterValueKind kind, PropertyMappingDescriptor? mapping, ImmutableArray<DiagnosticInfo> diagnostics,
        string? reason)
    {
        Kind = kind;
        Mapping = mapping;
        Diagnostics = diagnostics.IsDefault ? ImmutableArray<DiagnosticInfo>.Empty : diagnostics;
        Reason = reason;
    }

    public ParameterValueKind Kind { get; }

    /// <summary>The argument (<see cref="ParameterValueKind.Mapped"/>); its <c>DestPropertyName</c> is the parameter name.</summary>
    public PropertyMappingDescriptor? Mapping { get; }

    /// <summary>Diagnostics of the argument, reported only for the chosen constructor (IM0002 "no mapping for the pair", IM0004 candidates).</summary>
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>Why the parameter gets no value, for IM0009 (<see cref="ParameterValueKind.Unusable"/>, <see cref="ParameterValueKind.Ignored"/>).</summary>
    public string? Reason { get; }

    public static ParameterValue Mapped(PropertyMappingDescriptor mapping, ImmutableArray<DiagnosticInfo> diagnostics = default)
        => new(ParameterValueKind.Mapped, mapping, diagnostics, null);

    public static ParameterValue NoSource() => new(ParameterValueKind.NoSource, null, default, null);

    /// <param name="how">How it is ignored, for the message (<c>Ignore() in ForMember</c>, <c>[Ignore]</c> on …).</param>
    public static ParameterValue Ignored(string how) => new(ParameterValueKind.Ignored, null, default, how);

    public static ParameterValue Unusable(string reason) => new(ParameterValueKind.Unusable, null, default, reason);
}

/// <summary>
/// Finds the value of <paramref name="parameter"/>. <paramref name="sameNamedProperty"/> is the destination property with
/// the parameter's name (ignoring case), if any: its configuration (<c>ForMember</c>, <c>[Ignore]</c>) applies to the
/// parameter. With <paramref name="commit"/> <see langword="false"/> the constructor is only being compared with others:
/// nothing may be recorded (converters, diagnostics of the mapping).
/// </summary>
internal delegate ParameterValue ParameterResolver(IParameterSymbol parameter, IPropertySymbol? sameNamedProperty, bool commit);

/// <summary>Result of <see cref="ConstructorSelection.Choose"/>.</summary>
internal sealed class ConstructorChoice
{
    public static readonly ConstructorChoice Initializer = new(false, false, ImmutableArray<PropertyMappingDescriptor>.Empty,
        ImmutableArray<DiagnosticInfo>.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public ConstructorChoice(bool usesConstructor, bool failed, ImmutableArray<PropertyMappingDescriptor> arguments,
        ImmutableArray<DiagnosticInfo> diagnostics, HashSet<string> setByConstructor)
    {
        UsesConstructor = usesConstructor;
        Failed = failed;
        Arguments = arguments;
        Diagnostics = diagnostics;
        SetByConstructor = setByConstructor;
    }

    /// <summary>The destination is created by a constructor with parameters (<see cref="MappingDescriptor.UsesConstructor"/>).</summary>
    public bool UsesConstructor { get; }

    /// <summary>No constructor can be called (IM0009, IM0017, IM0018 in <see cref="Diagnostics"/>): the mapping is not generated.</summary>
    public bool Failed { get; }

    /// <summary>The arguments of the chosen constructor, in parameter order; parameters that take their default value are left out.</summary>
    public ImmutableArray<PropertyMappingDescriptor> Arguments { get; }

    /// <summary>Diagnostics of the choice and of the arguments.</summary>
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>
    /// The parameter names of the chosen constructor (ignoring case): a property with such a name is set by the constructor,
    /// so the initializer leaves it out and IM0001 is not reported for it; <c>[MapProperty]</c> may name a parameter.
    /// </summary>
    public HashSet<string> SetByConstructor { get; }
}

/// <summary>
/// How the generated code creates the destination (issue #24). With an accessible parameterless constructor (or a struct)
/// as before: <c>new D { … }</c>. Otherwise with a constructor: of the accessible constructors whose every parameter gets a
/// value (a source value, or the default value), the one with the most parameters mapped from the source, then with the
/// most parameters; a tie is IM0017 (the order of declaration does not decide: reordering code would change the mapping
/// silently). When no constructor can be called, IM0009 for the parameters of the one that comes closest (the most
/// parameters mapped, then the fewest without a value, then the first declared), or IM0018 when none is accessible. No
/// attribute in the destination type chooses the constructor: domain types must not depend on IronMapper.
/// </summary>
internal static class ConstructorSelection
{
    /// <summary>
    /// The destination is created with an object initializer: a struct (it always has a parameterless constructor), or a
    /// class with a parameterless constructor the generated code can call.
    /// </summary>
    public static bool UsesInitializer(INamedTypeSymbol dest, Compilation compilation)
        => dest.IsValueType
           || dest.InstanceConstructors.Any(c => c.Parameters.Length == 0 && IsAccessible(c, compilation));

    /// <param name="compilation">The compilation (accessibility).</param>
    /// <param name="source">The source type (messages).</param>
    /// <param name="dest">The destination type.</param>
    /// <param name="resolve">Finds the value of a parameter (the analyzer of the mapping).</param>
    /// <param name="origin">The definition of the mapping: IM0017 and IM0018 point here, IM0009 too unless
    /// <paramref name="atParameter"/>.</param>
    /// <param name="atParameter">IM0009 points to the parameter when it is in source (attribute mappings, as IM0001 points to
    /// the destination property); a profile's IM0009 points to the <c>CreateMap</c> call, where it is fixed.</param>
    /// <param name="hint">The hint of IM0009 for a parameter without a source, by how the mapping is declared.</param>
    public static ConstructorChoice Choose(Compilation compilation, INamedTypeSymbol source, INamedTypeSymbol dest,
        ParameterResolver resolve, LocationInfo? origin, bool atParameter, Func<IParameterSymbol, IPropertySymbol?, string> hint)
    {
        if (UsesInitializer(dest, compilation))
        {
            return ConstructorChoice.Initializer;
        }

        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in SymbolHelpers.GetPublicReadableProperties(dest))
        {
            // The most derived property wins (GetPublicReadableProperties starts with the type itself).
            if (!properties.ContainsKey(property.Name))
            {
                properties[property.Name] = property;
            }
        }

        IPropertySymbol? SameNamed(IParameterSymbol parameter) => properties.TryGetValue(parameter.Name, out var p) ? p : null;

        var candidates = dest.InstanceConstructors.Where(c => IsCandidate(c, dest, compilation)).ToList();
        if (candidates.Count == 0)
        {
            return Failure(new DiagnosticInfo(DiagnosticDescriptors.NoAccessibleConstructor, source.ToDisplayString(), dest.ToDisplayString())
                .WithLocation(origin));
        }

        // Compare without recording anything.
        var rated = candidates
            .Select((constructor, index) =>
            {
                var values = constructor.Parameters.Select(p => resolve(p, SameNamed(p), commit: false)).ToList();
                int mapped = values.Count(v => v.Kind == ParameterValueKind.Mapped);
                int missing = constructor.Parameters.Where((p, i) => !HasValue(p, values[i])).Count();
                return (constructor, index, mapped, missing);
            })
            .ToList();

        var callable = rated.Where(r => r.missing == 0).ToList();
        if (callable.Count > 0)
        {
            int bestMapped = callable.Max(r => r.mapped);
            int bestCount = callable.Where(r => r.mapped == bestMapped).Max(r => r.constructor.Parameters.Length);
            var best = callable.Where(r => r.mapped == bestMapped && r.constructor.Parameters.Length == bestCount).ToList();
            if (best.Count > 1)
            {
                return Failure(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, source.ToDisplayString(),
                        dest.ToDisplayString(), string.Join(" and ", best.Select(r => $"'{Display(r.constructor)}'")),
                        bestMapped.ToString(System.Globalization.CultureInfo.InvariantCulture), dest.Name)
                    .WithLocation(origin));
            }

            return Build(best[0].constructor);
        }

        // Nothing can be called: IM0009 for the parameters of the constructor that comes closest.
        var closest = rated.OrderByDescending(r => r.mapped).ThenBy(r => r.missing).ThenBy(r => r.index).First().constructor;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var parameter in closest.Parameters)
        {
            var sameNamed = SameNamed(parameter);
            var value = resolve(parameter, sameNamed, commit: false);
            if (HasValue(parameter, value))
            {
                continue;
            }

            string reason = value.Kind switch
            {
                ParameterValueKind.Unusable => value.Reason!,
                ParameterValueKind.Ignored => $"it is ignored ({value.Reason}) and has no default value. Map it instead.",
                _ => $"'{source.Name}' has no property named '{parameter.Name}' (names are compared ignoring case), and the "
                     + $"parameter has no default value. {hint(parameter, sameNamed)}",
            };
            if (candidates.Count > 1)
            {
                reason += $" (Constructor '{Display(closest)}', the closest of {candidates.Count} accessible constructors.)";
            }

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ConstructorParameterNotMapped, parameter.Name,
                    dest.ToDisplayString(), reason)
                .WithLocation((atParameter ? LocationInfo.From(parameter) : null) ?? origin));
        }

        return new ConstructorChoice(true, true, ImmutableArray<PropertyMappingDescriptor>.Empty, diagnostics.ToImmutable(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        ConstructorChoice Build(IMethodSymbol constructor)
        {
            var arguments = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
            var found = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            var setByConstructor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool setsRequiredMembers = constructor.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
            foreach (var parameter in constructor.Parameters)
            {
                var sameNamed = SameNamed(parameter);
                var value = resolve(parameter, sameNamed, commit: true);
                if (value.Kind == ParameterValueKind.Mapped)
                {
                    arguments.Add(value.Mapping!);
                    found.AddRange(value.Diagnostics);
                }

                // A required property must still be set in the initializer unless the constructor says it sets it (CS9035).
                if (sameNamed is not { IsRequired: true } || setsRequiredMembers)
                {
                    setByConstructor.Add(parameter.Name);
                }
            }

            return new ConstructorChoice(true, false, arguments.ToImmutable(), found.ToImmutable(), setByConstructor);
        }
    }

    /// <summary>The parameter gets a value: a source value, or its default value when nothing maps it or it is ignored.</summary>
    private static bool HasValue(IParameterSymbol parameter, ParameterValue value)
        => value.Kind == ParameterValueKind.Mapped
           || (value.Kind is ParameterValueKind.NoSource or ParameterValueKind.Ignored && (parameter.IsOptional || parameter.IsParams));

    private static ConstructorChoice Failure(DiagnosticInfo diagnostic)
        => new(true, true, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray.Create(diagnostic),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// A constructor the generated code can call with values: accessible, no <c>ref</c>/<c>out</c> parameter, not the copy
    /// constructor of a record.
    /// </summary>
    private static bool IsCandidate(IMethodSymbol constructor, INamedTypeSymbol dest, Compilation compilation)
    {
        if (!IsAccessible(constructor, compilation))
        {
            return false;
        }

        if (constructor.Parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter))
        {
            return false;
        }

        return !(dest.IsRecord && constructor.Parameters.Length == 1
                 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type.OriginalDefinition, dest.OriginalDefinition));
    }

    // The generated class is in the user's assembly: public and internal (also protected internal) constructors.
    private static bool IsAccessible(IMethodSymbol constructor, Compilation compilation)
        => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly);

    // D(int, string?)
    private static string Display(IMethodSymbol constructor)
        => $"{constructor.ContainingType.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Type.ToDisplayString()))})";
}
