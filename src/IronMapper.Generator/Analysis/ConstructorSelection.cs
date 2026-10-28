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

/// <summary>Result of <see cref="ConstructorSelection.Choose"/> and <see cref="ConstructorSelection.ForFactory"/>.</summary>
internal sealed class ConstructorChoice
{
    public static readonly ConstructorChoice Initializer = new(false, false, ImmutableArray<PropertyMappingDescriptor>.Empty,
        ImmutableArray<DiagnosticInfo>.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ImmutableArray<string>.Empty);

    public ConstructorChoice(bool usesConstructor, bool failed, ImmutableArray<PropertyMappingDescriptor> arguments,
        ImmutableArray<DiagnosticInfo> diagnostics, HashSet<string> setByConstructor, ImmutableArray<string> parameterNames,
        bool setsRequiredMembers = false)
    {
        SetsRequiredMembers = setsRequiredMembers;
        UsesConstructor = usesConstructor;
        Failed = failed;
        Arguments = arguments;
        Diagnostics = diagnostics;
        SetByConstructor = setByConstructor;
        ParameterNames = parameterNames;
    }

    /// <summary>The destination is created by a constructor with parameters or a factory (<see cref="MappingDescriptor.UsesConstructor"/>).</summary>
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

    /// <summary>
    /// The parameter names of the chosen constructor or factory; when no constructor can be called, those of every
    /// accessible constructor. <c>ForCtorParam</c> must name one of them (IM0019).
    /// </summary>
    public ImmutableArray<string> ParameterNames { get; }

    /// <summary>The chosen constructor has <c>[SetsRequiredMembers]</c>: the initializer need not set the <c>required</c>
    /// properties (<see cref="ConstructorSelection.SetsRequiredMembers"/>).</summary>
    public bool SetsRequiredMembers { get; }
}

/// <summary>
/// How the generated code creates the destination (issue #24). With an accessible parameterless constructor (or a struct)
/// as before: <c>new D { … }</c>. Otherwise with a constructor: of the accessible constructors whose every parameter gets a
/// value (a source value, or the default value), the one with the most parameters mapped from the source, then with the
/// most parameters; a tie is IM0017 (the order of declaration does not decide: reordering code would change the mapping
/// silently). When no constructor can be called, IM0009 for the parameters of the one that comes closest (the most
/// parameters mapped, then the fewest without a value, then the first declared), or IM0018 when none is accessible. No
/// attribute in the destination type chooses the constructor: domain types must not depend on IronMapper; a profile
/// chooses a factory or a constructor with <c>ConstructWith</c> (<see cref="ForFactory"/>).
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

    /// <summary>
    /// The constructor that creates the destination has <c>[SetsRequiredMembers]</c> (the chosen one, or the parameterless
    /// one of an object initializer), so a <c>required</c> property the mapping gives no value is not CS9035 (IM0026).
    /// </summary>
    public static bool SetsRequiredMembers(INamedTypeSymbol dest, ConstructorChoice construction)
        => construction.UsesConstructor
            ? construction.SetsRequiredMembers
            : dest.InstanceConstructors.Any(c => c.Parameters.Length == 0 && HasSetsRequiredMembers(c));

    private static bool HasSetsRequiredMembers(IMethodSymbol method)
        => method.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

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

        var properties = DestinationProperties(dest);
        IPropertySymbol? SameNamed(IParameterSymbol parameter) => properties.TryGetValue(parameter.Name, out var p) ? p : null;

        var candidates = dest.InstanceConstructors.Where(c => IsCandidate(c, dest, compilation)).ToList();
        if (candidates.Count == 0)
        {
            return Failure(new DiagnosticInfo(DiagnosticDescriptors.NoAccessibleConstructor, source.ToDisplayString(), dest.ToDisplayString(),
                    source.Name, dest.Name)
                .WithLocation(origin));
        }

        // Compare without recording anything.
        var rated = candidates
            .Select((constructor, index) =>
            {
                var values = constructor.Parameters.Select(p => resolve(p, SameNamed(p), commit: false)).ToList();
                int mapped = values.Count(v => v.Kind == ParameterValueKind.Mapped);
                int missing = constructor.Parameters.Where((p, i) => !HasValue(p, values[i])).Count();
                bool converts = values.Any(v => v.Mapping is { } m && NeedsPairConversion(m));
                return (constructor, index, mapped, missing, converts);
            })
            .ToList();

        // A value converted by the mapping of its pair (Guid → UserId) depends on the other mappings of the compilation,
        // which the analysis of one mapping does not know: the constructors that need no such conversion are chosen from
        // first, the others only when none of those can be called. A pair mapping added elsewhere never changes a choice.
        var callable = rated.Where(r => r.missing == 0 && !r.converts).ToList();
        if (callable.Count == 0)
        {
            callable = rated.Where(r => r.missing == 0).ToList();
        }

        if (callable.Count > 0)
        {
            int bestMapped = callable.Max(r => r.mapped);
            int bestCount = callable.Where(r => r.mapped == bestMapped).Max(r => r.constructor.Parameters.Length);
            var best = callable.Where(r => r.mapped == bestMapped && r.constructor.Parameters.Length == bestCount).ToList();
            if (best.Count > 1)
            {
                return Failure(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, source.ToDisplayString(),
                        dest.ToDisplayString(), string.Join(" and ", best.Select(r => $"'{Display(r.constructor)}'")),
                        bestMapped.ToString(System.Globalization.CultureInfo.InvariantCulture), LambdaCalling(best[0].constructor))
                    .WithLocation(origin));
            }

            return Build(best[0].constructor, resolve, SameNamed, isConstructor: true);
        }

        // Nothing can be called: IM0009 for the parameters of the constructor that comes closest.
        var closest = rated.OrderByDescending(r => r.mapped).ThenBy(r => r.missing).ThenBy(r => r.index).First().constructor;
        string? closestOf = candidates.Count > 1
            ? $" (Constructor '{Display(closest)}', the closest of {candidates.Count} accessible constructors.)"
            : null;
        return new ConstructorChoice(true, true, ImmutableArray<PropertyMappingDescriptor>.Empty,
            Missing(closest, "the constructor", closestOf, source, dest, resolve, SameNamed, origin, atParameter, hint),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            candidates.SelectMany(c => c.Parameters).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray());
    }

    /// <summary>
    /// The destination is created by <paramref name="factory"/> (<c>ConstructWith</c> in a profile: a static method, or the
    /// method of a lambda): its parameters get their values as those of a constructor, and a parameter without one is
    /// IM0009. Every parameter counts as setting the destination property of the same name: the factory returns the
    /// object, no initializer follows it.
    /// </summary>
    /// <param name="factory">The method that creates the destination.</param>
    /// <param name="what">The factory in messages: <c>factory 'D.Restore'</c>, <c>the lambda of ConstructWith</c>.</param>
    /// <param name="source">The source type (messages).</param>
    /// <param name="dest">The destination type.</param>
    /// <param name="resolve">Finds the value of a parameter (the analyzer of the profile).</param>
    /// <param name="origin">Where IM0009 points (the <c>CreateMap</c> call).</param>
    /// <param name="hint">The hint of IM0009 for a parameter without a source.</param>
    public static ConstructorChoice ForFactory(IMethodSymbol factory, string what, INamedTypeSymbol source,
        INamedTypeSymbol dest, ParameterResolver resolve, LocationInfo? origin, Func<IParameterSymbol, IPropertySymbol?, string> hint)
    {
        var properties = DestinationProperties(dest);
        IPropertySymbol? SameNamed(IParameterSymbol parameter) => properties.TryGetValue(parameter.Name, out var p) ? p : null;

        var missing = Missing(factory, what, null, source, dest, resolve, SameNamed, origin, atParameter: false, hint);
        if (missing.Length > 0)
        {
            return new ConstructorChoice(true, true, ImmutableArray<PropertyMappingDescriptor>.Empty, missing,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), factory.Parameters.Select(p => p.Name).ToImmutableArray());
        }

        return Build(factory, resolve, SameNamed, isConstructor: false);
    }

    // A value converted by a mapping of this compilation (PropertyTypeCheck.CheckValueConversion), not yet known to exist.
    private static bool NeedsPairConversion(PropertyMappingDescriptor mapping)
        => mapping.IsValueConversion && mapping.MappingPairs.Length + mapping.LiftedPairs.Length > 0;

    /// <summary>
    /// A constructor or factory parameter the generated code can pass a value to: no <c>ref</c>, <c>out</c> or
    /// <c>ref readonly</c>.
    /// </summary>
    public static bool IsPassedByValue(IParameterSymbol parameter)
        => parameter.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter);

    // The readable properties of the destination by name, ignoring case; the most derived one wins
    // (GetPublicReadableProperties starts with the type itself).
    private static Dictionary<string, IPropertySymbol> DestinationProperties(INamedTypeSymbol dest)
    {
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in SymbolHelpers.GetPublicReadableProperties(dest))
        {
            if (!properties.ContainsKey(property.Name))
            {
                properties[property.Name] = property;
            }
        }

        return properties;
    }

    // IM0009 for each parameter of `method` that gets no value; `suffix` is appended to each reason.
    private static ImmutableArray<DiagnosticInfo> Missing(IMethodSymbol method, string what, string? suffix, INamedTypeSymbol source,
        INamedTypeSymbol dest, ParameterResolver resolve, Func<IParameterSymbol, IPropertySymbol?> sameNamedOf,
        LocationInfo? origin, bool atParameter, Func<IParameterSymbol, IPropertySymbol?, string> hint)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var parameter in method.Parameters)
        {
            var sameNamed = sameNamedOf(parameter);
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

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ConstructorParameterNotMapped, parameter.Name,
                    dest.ToDisplayString(), reason + suffix, what)
                .WithLocation((atParameter ? LocationInfo.From(parameter) : null) ?? origin));
        }

        return diagnostics.ToImmutable();
    }

    // The arguments of `method`, which can be called. A constructor sets the property of a parameter's name unless it is
    // required and the constructor has no [SetsRequiredMembers] (the initializer must set it, CS9035).
    private static ConstructorChoice Build(IMethodSymbol method, ParameterResolver resolve,
        Func<IParameterSymbol, IPropertySymbol?> sameNamedOf, bool isConstructor)
    {
        var arguments = ImmutableArray.CreateBuilder<PropertyMappingDescriptor>();
        var found = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var setByConstructor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool setsRequiredMembers = !isConstructor || HasSetsRequiredMembers(method);
        foreach (var parameter in method.Parameters)
        {
            var sameNamed = sameNamedOf(parameter);
            var value = resolve(parameter, sameNamed, commit: true);
            if (value.Kind == ParameterValueKind.Mapped)
            {
                arguments.Add(value.Mapping!);
                found.AddRange(value.Diagnostics);
            }

            if (sameNamed is not { IsRequired: true } || setsRequiredMembers)
            {
                setByConstructor.Add(parameter.Name);
            }
        }

        return new ConstructorChoice(true, false, arguments.ToImmutable(), found.ToImmutable(), setByConstructor,
            method.Parameters.Select(p => p.Name).ToImmutableArray(), setsRequiredMembers);
    }

    /// <summary>The parameter gets a value: a source value, or its default value when nothing maps it or it is ignored.</summary>
    private static bool HasValue(IParameterSymbol parameter, ParameterValue value)
        => value.Kind == ParameterValueKind.Mapped
           || (value.Kind is ParameterValueKind.NoSource or ParameterValueKind.Ignored && (parameter.IsOptional || parameter.IsParams));

    private static ConstructorChoice Failure(DiagnosticInfo diagnostic)
        => new(true, true, ImmutableArray<PropertyMappingDescriptor>.Empty, ImmutableArray.Create(diagnostic),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), ImmutableArray<string>.Empty);

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

        if (!constructor.Parameters.All(IsPassedByValue))
        {
            return false;
        }

        return !(dest.IsRecord && constructor.Parameters.Length == 1
                 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type.OriginalDefinition, dest.OriginalDefinition));
    }

    // The generated class is in the user's assembly: public and internal (also protected internal) constructors.
    private static bool IsAccessible(IMethodSymbol constructor, Compilation compilation)
        => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly);

    // (int from, int to) => new D(from, to): the lambda of ConstructWith that chooses the constructor.
    private static string LambdaCalling(IMethodSymbol constructor)
        => $"({string.Join(", ", constructor.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))}) => "
           + $"new {constructor.ContainingType.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Name))})";

    // D(int, string?)
    private static string Display(IMethodSymbol constructor)
        => $"{constructor.ContainingType.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Type.ToDisplayString()))})";
}
