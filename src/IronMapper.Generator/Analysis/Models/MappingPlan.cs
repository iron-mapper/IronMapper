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
    private Dictionary<string, string>? _methodByPair;

    /// <summary>One entry per mapping definition, sorted by <see cref="MappingPlanEntry.Origin"/>.</summary>
    public ImmutableArray<MappingPlanEntry> Entries { get; }

    /// <summary>IM0007, IM0013 and IM0014, reported once per compilation.</summary>
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>Initialises a new <see cref="MappingPlan"/>.</summary>
    public MappingPlan(ImmutableArray<MappingPlanEntry> entries, ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Entries = entries;
        Diagnostics = diagnostics;
    }

    /// <summary>Returns the entry for the definition <paramref name="origin"/>, or <see langword="null"/>.</summary>
    public MappingPlanEntry? Find(string origin)
    {
        if (_byOrigin is null)
        {
            var byOrigin = new Dictionary<string, MappingPlanEntry>(StringComparer.Ordinal);
            foreach (var entry in Entries) byOrigin[entry.Origin] = entry;
            _byOrigin = byOrigin;
        }

        return _byOrigin.TryGetValue(origin, out var found) ? found : null;
    }

    /// <summary>Returns the generated method name for the type pair <paramref name="pairKey"/>, or <see langword="null"/> when it is not generated.</summary>
    public string? GetMethodName(string pairKey)
    {
        if (_methodByPair is null)
        {
            var methodByPair = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
                if (entry.Emit) methodByPair[entry.PairKey] = entry.MethodName;
            _methodByPair = methodByPair;
        }

        return _methodByPair.TryGetValue(pairKey, out var name) ? name : null;
    }

    /// <inheritdoc/>
    public bool Equals(MappingPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Entries.Length != other.Entries.Length || Diagnostics.Length != other.Diagnostics.Length) return false;
        for (var i = 0; i < Entries.Length; i++)
            if (!Entries[i].Equals(other.Entries[i])) return false;
        for (var i = 0; i < Diagnostics.Length; i++)
            if (!Diagnostics[i].Equals(other.Diagnostics[i])) return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingPlan);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var entry in Entries) hash = hash * 31 + entry.GetHashCode();
        foreach (var diagnostic in Diagnostics) hash = hash * 31 + diagnostic.GetHashCode();
        return hash;
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

    /// <summary>Initialises a new <see cref="MappingPlanEntry"/>.</summary>
    public MappingPlanEntry(string origin, string pairKey, bool emit, string methodName, string hintName, string helperScope)
    {
        Origin = origin;
        PairKey = pairKey;
        Emit = emit;
        MethodName = methodName;
        HintName = hintName;
        HelperScope = helperScope;
    }

    /// <inheritdoc/>
    public bool Equals(MappingPlanEntry? other)
        => other is not null
           && Origin == other.Origin
           && PairKey == other.PairKey
           && Emit == other.Emit
           && MethodName == other.MethodName
           && HintName == other.HintName
           && HelperScope == other.HelperScope;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingPlanEntry);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + Origin.GetHashCode();
        hash = hash * 31 + Emit.GetHashCode();
        hash = hash * 31 + MethodName.GetHashCode();
        hash = hash * 31 + HintName.GetHashCode();
        hash = hash * 31 + HelperScope.GetHashCode();
        return hash;
    }
}
