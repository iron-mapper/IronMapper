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

    /// <summary>
    /// When <see langword="true"/>, the converter has no state and the generated code reuses one instance
    /// (<c>ConverterCache&lt;T&gt;.Instance</c>); otherwise it creates one with <c>new</c> on every call.
    /// </summary>
    public bool ConverterIsCached { get; }

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

    /// <summary>
    /// When non-null, this property was resolved via <c>IncludeMembers</c> flattening.
    /// Holds the FQN of the nested property's type so that value transformers can be
    /// applied by the code emitter even though <see cref="LambdaBody"/> is also set.
    /// </summary>
    public string? IncludedMemberTypeFqn { get; }

    /// <summary>
    /// Type pair of the collection element mapping (<c>{sourceFqn}-&gt;{destFqn}</c>), used to call the element
    /// mapping by its resolved name when <c>MapTo{Name}</c> was qualified because of a collision.
    /// <see langword="null"/> when <see cref="CollectionElementMapMethod"/> is not set.
    /// </summary>
    public string? CollectionElementPairKey { get; }

    /// <summary>
    /// How the source collection is read (<c>"Array"</c>, <c>"List"</c>, <c>"IndexedList"</c>, <c>"Counted"</c>,
    /// <c>"Enumerable"</c>; see <see cref="SymbolHelpers.GetCollectionSourceKind"/>), or <see langword="null"/> when
    /// <see cref="CollectionElementMapMethod"/> is not set.
    /// </summary>
    public string? CollectionSourceKind { get; }

    /// <summary>Fully qualified type of the source collection property (no top-level <c>?</c>), or <see langword="null"/>.</summary>
    public string? CollectionSourceTypeFqn { get; }

    /// <summary>Fully qualified destination element type, or <see langword="null"/>.</summary>
    public string? CollectionDestElementFqn { get; }

    /// <summary>Initialises a new <see cref="PropertyMappingDescriptor"/>.</summary>
    /// <param name="sourcePropertyName">Name of the property to read on the source object.</param>
    /// <param name="destPropertyName">Name of the property to write on the destination object.</param>
    /// <param name="isIgnored">When <see langword="true"/> the destination member is skipped entirely.</param>
    /// <param name="converterType">Fully-qualified name of the <c>ITypeConverter</c> to use, or <see langword="null"/>.</param>
    /// <param name="needsNullCheck">When <see langword="true"/> the generated code wraps the read in a null-check.</param>
    /// <param name="lambdaBody">Optional verbatim C# expression to emit for the right-hand side (uses "source" as the source variable).</param>
    /// <param name="isInitOnly">When <see langword="true"/> the destination setter is <c>init</c>-only; the in-place mapper skips this property.</param>
    /// <param name="sourcePropertyTypeFqn">The <c>global::</c>-prefixed FQN of the source property type, or <see langword="null"/> for ForMember-configured properties.</param>
    /// <param name="includedMemberTypeFqn">The FQN of the nested property type when this property was resolved via IncludeMembers, or <see langword="null"/> otherwise.</param>
    /// <param name="collection">The element mapping when source and destination are collections of different element types, or <see langword="null"/>.</param>
    /// <param name="converterIsCached">When <see langword="true"/> the generated code reuses one instance of <paramref name="converterType"/>.</param>
    public PropertyMappingDescriptor(
        string sourcePropertyName,
        string destPropertyName,
        bool isIgnored,
        string? converterType,
        bool needsNullCheck,
        string? lambdaBody = null,
        bool isInitOnly = false,
        string? sourcePropertyTypeFqn = null,
        string? includedMemberTypeFqn = null,
        CollectionMapping? collection = null,
        bool converterIsCached = false)
    {
        SourcePropertyName = sourcePropertyName;
        DestPropertyName = destPropertyName;
        IsIgnored = isIgnored;
        ConverterType = converterType;
        ConverterIsCached = converterIsCached;
        NeedsNullCheck = needsNullCheck;
        LambdaBody = lambdaBody;
        IsInitOnly = isInitOnly;
        SourcePropertyTypeFqn = sourcePropertyTypeFqn;
        IncludedMemberTypeFqn = includedMemberTypeFqn;
        CollectionElementMapMethod = collection?.ElementMethod;
        CollectionOutputType = collection?.OutputType;
        CollectionElementPairKey = collection?.PairKey;
        CollectionSourceKind = collection?.SourceKind;
        CollectionSourceTypeFqn = collection?.SourceTypeFqn;
        CollectionDestElementFqn = collection?.DestElementFqn;
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
            && ConverterIsCached == other.ConverterIsCached
            && NeedsNullCheck == other.NeedsNullCheck
            && LambdaBody == other.LambdaBody
            && IsInitOnly == other.IsInitOnly
            && CollectionElementMapMethod == other.CollectionElementMapMethod
            && CollectionOutputType == other.CollectionOutputType
            && SourcePropertyTypeFqn == other.SourcePropertyTypeFqn
            && IncludedMemberTypeFqn == other.IncludedMemberTypeFqn
            && CollectionElementPairKey == other.CollectionElementPairKey
            && CollectionSourceKind == other.CollectionSourceKind
            && CollectionSourceTypeFqn == other.CollectionSourceTypeFqn
            && CollectionDestElementFqn == other.CollectionDestElementFqn;
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
        hash = hash * 31 + ConverterIsCached.GetHashCode();
        hash = hash * 31 + NeedsNullCheck.GetHashCode();
        hash = hash * 31 + (LambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + IsInitOnly.GetHashCode();
        hash = hash * 31 + (CollectionElementMapMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionOutputType?.GetHashCode() ?? 0);
        hash = hash * 31 + (SourcePropertyTypeFqn?.GetHashCode() ?? 0);
        hash = hash * 31 + (IncludedMemberTypeFqn?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionElementPairKey?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionSourceKind?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionSourceTypeFqn?.GetHashCode() ?? 0);
        hash = hash * 31 + (CollectionDestElementFqn?.GetHashCode() ?? 0);
        return hash;
    }
}
