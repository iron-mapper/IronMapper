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

    /// <summary>
    /// When non-null, this is a C# expression (with "source" as the source object variable) that
    /// should be emitted verbatim as the right-hand side of the assignment.
    /// Produced by ForMember(..., opt => opt.MapFrom(src => ...)) in a MappingProfile.
    /// </summary>
    public string? LambdaBody { get; }

    /// <summary>Initialises a new <see cref="PropertyMappingDescriptor"/>.</summary>
    /// <param name="sourcePropertyName">Name of the property to read on the source object.</param>
    /// <param name="destPropertyName">Name of the property to write on the destination object.</param>
    /// <param name="isIgnored">When <see langword="true"/> the destination member is skipped entirely.</param>
    /// <param name="converterType">Fully-qualified name of the <c>ITypeConverter</c> to use, or <see langword="null"/>.</param>
    /// <param name="needsNullCheck">When <see langword="true"/> the generated code wraps the read in a null-check.</param>
    /// <param name="lambdaBody">Optional verbatim C# expression to emit for the right-hand side (uses "source" as the source variable).</param>
    public PropertyMappingDescriptor(
        string sourcePropertyName,
        string destPropertyName,
        bool isIgnored,
        string? converterType,
        bool needsNullCheck,
        string? lambdaBody = null)
    {
        SourcePropertyName = sourcePropertyName;
        DestPropertyName = destPropertyName;
        IsIgnored = isIgnored;
        ConverterType = converterType;
        NeedsNullCheck = needsNullCheck;
        LambdaBody = lambdaBody;
    }

    public bool Equals(PropertyMappingDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourcePropertyName == other.SourcePropertyName
            && DestPropertyName == other.DestPropertyName
            && IsIgnored == other.IsIgnored
            && ConverterType == other.ConverterType
            && NeedsNullCheck == other.NeedsNullCheck
            && LambdaBody == other.LambdaBody;
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
        hash = hash * 31 + (LambdaBody?.GetHashCode() ?? 0);
        return hash;
    }
}
