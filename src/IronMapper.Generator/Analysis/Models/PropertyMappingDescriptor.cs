using System;
using System.Collections.Generic;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>Describes how a single destination property is populated from a source property.</summary>
internal sealed class PropertyMappingDescriptor : IEquatable<PropertyMappingDescriptor>
{
    /// <summary>Name of the source property to read from.</summary>
    public string SourcePropertyName { get; }

    /// <summary>Name of the destination property to write to.</summary>
    public string DestPropertyName { get; }

    /// <summary>When true, this property is skipped; the destination member retains its default value.</summary>
    public bool IsIgnored { get; }

    /// <summary>Fully-qualified name of the ITypeConverter implementation, or null if not set.</summary>
    public string? ConverterType { get; }

    /// <summary>When true, the generated code should null-check the source member before reading.</summary>
    public bool NeedsNullCheck { get; }

    public PropertyMappingDescriptor(
        string sourcePropertyName,
        string destPropertyName,
        bool isIgnored,
        string? converterType,
        bool needsNullCheck)
    {
        SourcePropertyName = sourcePropertyName;
        DestPropertyName = destPropertyName;
        IsIgnored = isIgnored;
        ConverterType = converterType;
        NeedsNullCheck = needsNullCheck;
    }

    public bool Equals(PropertyMappingDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourcePropertyName == other.SourcePropertyName
            && DestPropertyName == other.DestPropertyName
            && IsIgnored == other.IsIgnored
            && ConverterType == other.ConverterType
            && NeedsNullCheck == other.NeedsNullCheck;
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyMappingDescriptor);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + SourcePropertyName.GetHashCode();
        hash = hash * 31 + DestPropertyName.GetHashCode();
        hash = hash * 31 + IsIgnored.GetHashCode();
        hash = hash * 31 + (ConverterType?.GetHashCode() ?? 0);
        hash = hash * 31 + NeedsNullCheck.GetHashCode();
        return hash;
    }
}
