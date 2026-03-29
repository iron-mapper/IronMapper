using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>Complete description of one source→destination mapping, consumed by the code emitter.</summary>
internal sealed class MappingDescriptor : IEquatable<MappingDescriptor>
{
    /// <summary>Simple name of the source type (without namespace).</summary>
    public string SourceTypeName { get; }

    /// <summary>Namespace of the source type, or null for the global namespace.</summary>
    public string? SourceNamespace { get; }

    /// <summary>Simple name of the destination type (without namespace).</summary>
    public string DestTypeName { get; }

    /// <summary>Namespace of the destination type, or null for the global namespace.</summary>
    public string? DestNamespace { get; }

    /// <summary>Per-property mapping instructions.</summary>
    public ImmutableArray<PropertyMappingDescriptor> PropertyMappings { get; }

    /// <summary>Diagnostics to report when this mapping is emitted.</summary>
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>True when at least one property uses a custom ITypeConverter.</summary>
    public bool HasCustomConverter { get; }

    /// <summary>Initialises a new <see cref="MappingDescriptor"/>.</summary>
    /// <param name="sourceTypeName">Simple (unqualified) name of the source type.</param>
    /// <param name="sourceNamespace">Namespace of the source type, or <see langword="null"/> for the global namespace.</param>
    /// <param name="destTypeName">Simple (unqualified) name of the destination type.</param>
    /// <param name="destNamespace">Namespace of the destination type, or <see langword="null"/> for the global namespace.</param>
    /// <param name="propertyMappings">Per-property mapping instructions in declaration order.</param>
    /// <param name="diagnostics">Diagnostics to surface when this descriptor is emitted.</param>
    /// <param name="hasCustomConverter">Whether any property mapping uses a custom <c>ITypeConverter</c>.</param>
    public MappingDescriptor(
        string sourceTypeName,
        string? sourceNamespace,
        string destTypeName,
        string? destNamespace,
        ImmutableArray<PropertyMappingDescriptor> propertyMappings,
        ImmutableArray<DiagnosticInfo> diagnostics,
        bool hasCustomConverter)
    {
        SourceTypeName = sourceTypeName;
        SourceNamespace = sourceNamespace;
        DestTypeName = destTypeName;
        DestNamespace = destNamespace;
        PropertyMappings = propertyMappings;
        Diagnostics = diagnostics;
        HasCustomConverter = hasCustomConverter;
    }

    public bool Equals(MappingDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (SourceTypeName != other.SourceTypeName
            || SourceNamespace != other.SourceNamespace
            || DestTypeName != other.DestTypeName
            || DestNamespace != other.DestNamespace
            || HasCustomConverter != other.HasCustomConverter)
            return false;

        if (PropertyMappings.Length != other.PropertyMappings.Length) return false;
        for (var i = 0; i < PropertyMappings.Length; i++)
            if (!PropertyMappings[i].Equals(other.PropertyMappings[i])) return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MappingDescriptor);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + SourceTypeName.GetHashCode();
        hash = hash * 31 + (SourceNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + DestTypeName.GetHashCode();
        hash = hash * 31 + (DestNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + HasCustomConverter.GetHashCode();
        foreach (var pm in PropertyMappings)
            hash = hash * 31 + pm.GetHashCode();
        return hash;
    }
}
