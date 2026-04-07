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

    /// <summary>
    /// When <see langword="true"/>, the destination property setter is <c>init</c>-only and cannot be
    /// assigned after object construction — the in-place mapper skips such properties silently.
    /// </summary>
    public bool IsInitOnly { get; }

    /// <summary>
    /// When non-null, the source property is a collection whose elements should be mapped individually.
    /// This is the name of the generated extension method to call on each element
    /// (e.g. <c>"MapToOrderDto"</c>).
    /// </summary>
    public string? CollectionElementMapMethod { get; }

    /// <summary>
    /// Indicates how the mapped collection should be materialised in the output:
    /// <c>"Array"</c>, <c>"List"</c>, or <c>"Enumerable"</c> (lazy sequence, no materialisation).
    /// <see langword="null"/> when <see cref="CollectionElementMapMethod"/> is not set.
    /// </summary>
    public string? CollectionOutputType { get; }

    /// <summary>
    /// The <c>global::</c>-prefixed fully-qualified type name of the source property.
    /// Only populated for name-matched properties (not ForMember-configured ones).
    /// Used by value transformers to match on type.
    /// </summary>
    public string? SourcePropertyTypeFqn { get; }

    /// <summary>Initialises a new <see cref="PropertyMappingDescriptor"/>.</summary>
    /// <param name="sourcePropertyName">Name of the property to read on the source object.</param>
    /// <param name="destPropertyName">Name of the property to write on the destination object.</param>
    /// <param name="isIgnored">When <see langword="true"/> the destination member is skipped entirely.</param>
    /// <param name="converterType">Fully-qualified name of the <c>ITypeConverter</c> to use, or <see langword="null"/>.</param>
    /// <param name="needsNullCheck">When <see langword="true"/> the generated code wraps the read in a null-check.</param>
    /// <param name="lambdaBody">Optional verbatim C# expression to emit for the right-hand side (uses "source" as the source variable).</param>
    /// <param name="isInitOnly">When <see langword="true"/> the destination setter is <c>init</c>-only; the in-place mapper skips this property.</param>
    /// <param name="collectionElementMapMethod">Name of the generated method used to map each collection element, or <see langword="null"/>.</param>
    /// <param name="collectionOutputType">Output materialisation kind: <c>"Array"</c>, <c>"List"</c>, <c>"Enumerable"</c>, or <see langword="null"/>.</param>
    /// <param name="sourcePropertyTypeFqn">The <c>global::</c>-prefixed FQN of the source property type, or <see langword="null"/> for ForMember-configured properties.</param>
    public PropertyMappingDescriptor(
        string sourcePropertyName,
        string destPropertyName,
        bool isIgnored,
        string? converterType,
        bool needsNullCheck,
        string? lambdaBody = null,
        bool isInitOnly = false,
        string? collectionElementMapMethod = null,
        string? collectionOutputType = null,
        string? sourcePropertyTypeFqn = null)
    {
        SourcePropertyName = sourcePropertyName;
        DestPropertyName = destPropertyName;
        IsIgnored = isIgnored;
        ConverterType = converterType;
        NeedsNullCheck = needsNullCheck;
        LambdaBody = lambdaBody;
        IsInitOnly = isInitOnly;
        CollectionElementMapMethod = collectionElementMapMethod;
        CollectionOutputType = collectionOutputType;
        SourcePropertyTypeFqn = sourcePropertyTypeFqn;
    }

    /// <inheritdoc/>
    public bool Equals(PropertyMappingDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourcePropertyName == other.SourcePropertyName
            && DestPropertyName == other.DestPropertyName
            && IsIgnored == other.IsIgnored
            && ConverterType == other.ConverterType
            && NeedsNullCheck == other.NeedsNullCheck
            && LambdaBody == other.LambdaBody
            && IsInitOnly == other.IsInitOnly
            && CollectionElementMapMethod == other.CollectionElementMapMethod
            && CollectionOutputType == other.CollectionOutputType
            && SourcePropertyTypeFqn == other.SourcePropertyTypeFqn;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PropertyMappingDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + SourcePropertyName.GetHashCode();
        hash = hash * 31 + DestPropertyName.GetHashCode();
        hash = hash * 31 + IsIgnored.GetHashCode();
        hash = hash * 31 + (ConverterType?.GetHashCode() ?? 0);
        hash = hash * 31 + NeedsNullCheck.GetHashCode();
        hash = hash * 31 + (LambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + IsInitOnly.GetHashCode();
        hash = hash * 31 + (CollectionElementMapMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionOutputType?.GetHashCode() ?? 0);
        hash = hash * 31 + (SourcePropertyTypeFqn?.GetHashCode() ?? 0);
        return hash;
    }
}
