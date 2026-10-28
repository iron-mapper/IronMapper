using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// Decisions that need all mappings of the compilation: which definition of a duplicated type pair is generated,
/// the method name of each mapping, unique hint names and helper-name scopes. Compared by value, so the
/// per-mapping outputs are re-emitted only when a decision changes.
/// </summary>
internal sealed class MappingPlan : IEquatable<MappingPlan>
{
    private Dictionary<string, MappingPlanEntry>? _byOrigin;
    private Dictionary<string, MappingPlanEntry>? _entryByPair;

    /// <summary>One entry per mapping definition, sorted by <see cref="MappingPlanEntry.Origin"/>.</summary>
    public EquatableArray<MappingPlanEntry> Entries { get; }

    /// <summary>IM0007, IM0013 and IM0014, reported once per compilation.</summary>
    public EquatableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>Initialises a new <see cref="MappingPlan"/>.</summary>
    public MappingPlan(EquatableArray<MappingPlanEntry> entries, EquatableArray<DiagnosticInfo> diagnostics)
    {
        Entries = entries;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// The copy used to generate code: the entries without <see cref="Diagnostics"/>. The diagnostics carry locations,
    /// which change when lines are added above a mapping; the generated files do not depend on them.
    /// </summary>
    public MappingPlan WithoutDiagnostics() => Diagnostics.Length == 0 ? this : new(Entries, EquatableArray<DiagnosticInfo>.Empty);

    /// <summary>Returns the entry for the definition <paramref name="origin"/>, or <see langword="null"/>.</summary>
    public MappingPlanEntry? Find(string origin)
    {
        if (_byOrigin is null)
        {
            var byOrigin = new Dictionary<string, MappingPlanEntry>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                byOrigin[entry.Origin] = entry;
            }

            _byOrigin = byOrigin;
        }

        return _byOrigin.TryGetValue(origin, out var found) ? found : null;
    }

    /// <summary>
    /// A mapping of one of the type pairs <paramref name="pairKeys"/> is generated (IM0002 for a collection element: the
    /// pairs of the source element type and its base types and interfaces, <see cref="PropertyTypeCheck.ElementPairKeys"/>).
    /// </summary>
    public bool GeneratesAny(IEnumerable<string> pairKeys)
    {
        foreach (string key in pairKeys)
        {
            if (GetMethodName(key) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the generated method name for the type pair <paramref name="pairKey"/>, or <see langword="null"/> when it is not generated.</summary>
    public string? GetMethodName(string pairKey) => FindGenerated(pairKey)?.MethodName;

    /// <summary>
    /// The generated entry of the first of <paramref name="pairKeys"/> that is generated (a nested object or a collection
    /// element: the pair of the source type first, then of its base types and interfaces), or <see langword="null"/>.
    /// </summary>
    public MappingPlanEntry? FindFirstGenerated(IEnumerable<string> pairKeys)
    {
        foreach (string key in pairKeys)
        {
            if (FindGenerated(key) is { } entry)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>The generated entry of the type pair <paramref name="pairKey"/>, or <see langword="null"/>.</summary>
    public MappingPlanEntry? FindGenerated(string pairKey)
    {
        if (_entryByPair is null)
        {
            var entryByPair = new Dictionary<string, MappingPlanEntry>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry.Emit)
                {
                    entryByPair[entry.PairKey] = entry;
                }
            }

            _entryByPair = entryByPair;
        }

        return _entryByPair.TryGetValue(pairKey, out var found) ? found : null;
    }

    /// <inheritdoc/>
    public bool Equals(MappingPlan? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Entries.Equals(other.Entries) && Diagnostics.Equals(other.Diagnostics);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingPlan);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Entries.GetHashCode() * 31 + Diagnostics.GetHashCode();
    }
}

/// <summary>The plan for one mapping definition (see <see cref="MappingDescriptor.Origin"/>).</summary>
internal sealed class MappingPlanEntry : IEquatable<MappingPlanEntry>
{
    /// <summary>Definition site, as in <see cref="MappingDescriptor.Origin"/>.</summary>
    public string Origin { get; }

    /// <summary>Type pair, as in <see cref="MappingDescriptor.PairKey"/>.</summary>
    public string PairKey { get; }

    /// <summary>False for a duplicate definition (IM0007) or an unresolvable name collision (IM0014).</summary>
    public bool Emit { get; }

    /// <summary>Name of <c>MapToX</c> and the in-place overload; <c>List</c>/<c>Array</c> are appended for the collection methods.</summary>
    public string MethodName { get; }

    /// <summary>Unique hint name of the generated file.</summary>
    public string HintName { get; }

    /// <summary>Suffix of hook and transformer helper names, e.g. <c>_User_UserDto</c>.</summary>
    public string HelperScope { get; }

    /// <summary>Accessibility of the generated methods (<c>public</c>/<c>internal</c>), as in <see cref="MappingDescriptor.MethodAccessibility"/>.</summary>
    public string MethodAccessibility { get; }

    /// <summary>Fully-qualified C# name of the source type (<c>global::...</c>), for the generated registration.</summary>
    public string SourceFullName { get; }

    /// <summary>Fully-qualified C# name of the destination type, for the generated registration.</summary>
    public string DestFullName { get; }

    /// <summary>The generated code of the mapping uses <c>ConverterCache&lt;T&gt;</c>, as in <see cref="MappingDescriptor.UsesConverterCache"/>.</summary>
    public bool UsesConverterCache { get; }

    /// <summary>Overloads set for the mapping, as in <see cref="MappingDescriptor.MethodOverrides"/>.</summary>
    public GeneratedMethodOverrides MethodOverrides { get; }

    /// <summary>
    /// Name of the private <c>…Core</c> methods: <c>MapToX(source)</c> and the in-place <c>MapToX(source, destination)</c>
    /// without the null checks, called by nested objects after their own check (see <see cref="Analysis.MappingPlanner"/>).
    /// Empty when not generated.
    /// </summary>
    public string CoreMethodName { get; }

    /// <summary>
    /// Another generated mapping (or this one) creates objects of this pair through <c>…Core(source)</c>: a nested object,
    /// a converted value, a constructor or factory argument. Otherwise <c>MapToX(source)</c> keeps the form of 2.0.0: one
    /// method with the null check and the body, and no <c>…Core</c>.
    /// </summary>
    public bool UsesCore { get; }

    /// <summary>
    /// The in-place update of a generated mapping updates an existing nested object of this pair through
    /// <c>…Core(source, destination)</c>. Otherwise the in-place body stays in the in-place overload (or in the private
    /// <c>MapInto</c> of the registration), as in 2.0.0.
    /// </summary>
    public bool UsesInPlaceCore { get; }

    /// <summary>
    /// The in-place overload changes nothing for the caller (<see cref="MappingDescriptor.InPlaceNoOpReason"/>): it is
    /// <c>[Obsolete]</c>, and the registration for <c>IMapper</c> takes a private <c>MapInto</c> that throws.
    /// </summary>
    public bool InPlaceIsNoOp { get; }

    /// <summary>The generated code of the mapping calls <c>NestedObjectIsNull&lt;T&gt;</c> (a nested object checked for <see langword="null"/>).</summary>
    public bool UsesNestedObjectCheck { get; }

    /// <summary>
    /// A nested object of this pair may be updated by the in-place overload (<see cref="MappingDescriptor.CanUpdateInPlace"/>);
    /// otherwise it is assigned a new object.
    /// </summary>
    public bool CanUpdateInPlace { get; }

    /// <summary>Initialises a new <see cref="MappingPlanEntry"/>.</summary>
    public MappingPlanEntry(string origin, string pairKey, bool emit, string methodName, string hintName, string helperScope,
        string methodAccessibility, string sourceFullName, string destFullName, bool usesConverterCache = false,
        GeneratedMethodOverrides methodOverrides = default, string coreMethodName = "", bool usesNestedObjectCheck = false,
        bool canUpdateInPlace = false, bool usesCore = false, bool usesInPlaceCore = false, bool inPlaceIsNoOp = false)
    {
        CoreMethodName = coreMethodName;
        UsesCore = usesCore;
        UsesInPlaceCore = usesInPlaceCore;
        InPlaceIsNoOp = inPlaceIsNoOp;
        UsesNestedObjectCheck = usesNestedObjectCheck;
        CanUpdateInPlace = canUpdateInPlace;
        Origin = origin;
        PairKey = pairKey;
        Emit = emit;
        MethodName = methodName;
        HintName = hintName;
        HelperScope = helperScope;
        MethodAccessibility = methodAccessibility;
        SourceFullName = sourceFullName;
        DestFullName = destFullName;
        UsesConverterCache = usesConverterCache;
        MethodOverrides = methodOverrides;
    }

    /// <inheritdoc/>
    public bool Equals(MappingPlanEntry? other)
        => other is not null
           && Origin == other.Origin
           && PairKey == other.PairKey
           && Emit == other.Emit
           && MethodName == other.MethodName
           && HintName == other.HintName
           && HelperScope == other.HelperScope
           && MethodAccessibility == other.MethodAccessibility
           && SourceFullName == other.SourceFullName
           && DestFullName == other.DestFullName
           && UsesConverterCache == other.UsesConverterCache
           && MethodOverrides.Equals(other.MethodOverrides)
           && CoreMethodName == other.CoreMethodName
           && UsesNestedObjectCheck == other.UsesNestedObjectCheck
           && CanUpdateInPlace == other.CanUpdateInPlace
           && UsesCore == other.UsesCore
           && UsesInPlaceCore == other.UsesInPlaceCore
           && InPlaceIsNoOp == other.InPlaceIsNoOp;

    /// <summary>A copy with <see cref="UsesCore"/> and <see cref="UsesInPlaceCore"/> set (see <see cref="Analysis.MappingPlanner"/>).</summary>
    public MappingPlanEntry WithCoreUse(bool usesCore, bool usesInPlaceCore)
        => usesCore == UsesCore && usesInPlaceCore == UsesInPlaceCore
            ? this
            : new(Origin, PairKey, Emit, MethodName, HintName, HelperScope, MethodAccessibility, SourceFullName, DestFullName,
                UsesConverterCache, MethodOverrides, CoreMethodName, UsesNestedObjectCheck, CanUpdateInPlace, usesCore,
                usesInPlaceCore, InPlaceIsNoOp);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingPlanEntry);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + Origin.GetHashCode();
        hash = hash * 31 + PairKey.GetHashCode();
        hash = hash * 31 + Emit.GetHashCode();
        hash = hash * 31 + MethodName.GetHashCode();
        hash = hash * 31 + HintName.GetHashCode();
        hash = hash * 31 + HelperScope.GetHashCode();
        hash = hash * 31 + MethodAccessibility.GetHashCode();
        hash = hash * 31 + SourceFullName.GetHashCode();
        hash = hash * 31 + DestFullName.GetHashCode();
        hash = hash * 31 + UsesConverterCache.GetHashCode();
        hash = hash * 31 + MethodOverrides.GetHashCode();
        hash = hash * 31 + CoreMethodName.GetHashCode();
        hash = hash * 31 + UsesNestedObjectCheck.GetHashCode();
        hash = hash * 31 + CanUpdateInPlace.GetHashCode();
        hash = hash * 31 + UsesCore.GetHashCode();
        hash = hash * 31 + UsesInPlaceCore.GetHashCode();
        hash = hash * 31 + InPlaceIsNoOp.GetHashCode();
        return hash;
    }
}
