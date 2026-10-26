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

    /// <summary>
    /// The type pairs of the collection element or of the nested object (<see cref="NestedMapMethod"/>) of which this
    /// compilation must generate one, otherwise the property is not generated and IM0002 is reported
    /// (<see cref="MappingPlan.GeneratesAny"/>). A nested object calls the <c>…Core</c> of the first generated one. Empty when
    /// the value needs no mapping of this compilation (not a mapped collection or nested object, or a referenced
    /// assembly maps it).
    /// </summary>
    public EquatableArray<string> MappingPairs { get; }

    /// <summary>
    /// When non-null, the property is a nested object mapped by a mapping of its type pair (<see cref="MappingPairs"/>,
    /// or a referenced assembly's mapping): <c>MapTo{DestType}</c>, called as an extension method when the mapping is not
    /// generated in this compilation.
    /// </summary>
    public string? NestedMapMethod { get; }

    /// <summary>
    /// How a nested object (<see cref="NestedMapMethod"/>) is passed to its mapping, by the nullable annotations of the
    /// source and the destination (<see cref="Analysis.NullHandling"/>); <see langword="null"/> for other properties.
    /// Collection elements always call the public method (<c>ArgumentNullException</c> for a <see langword="null"/>
    /// element, as in 2.0.0).
    /// </summary>
    public string? NullHandling { get; }

    /// <summary>
    /// The in-place overload may update the existing nested object (<see cref="NestedMapMethod"/>): when
    /// <c>destination.{Property}</c> is not <see langword="null"/>, its properties are mapped into it by the in-place
    /// <c>…Core</c> of the pair (also needs <see cref="MappingPlanEntry.CanUpdateInPlace"/> of the pair's mapping) or by
    /// the public in-place overload of a referenced assembly's mapping, so a tracked EF navigation keeps its instance;
    /// otherwise a new object is assigned. <see langword="false"/> for a struct (the getter returns a copy), a property
    /// without an accessible getter, and a referenced assembly's mapping without the in-place overload.
    /// </summary>
    public bool NestedUpdatesInPlace { get; }

    /// <summary>
    /// Fully qualified source type of a nested object (<see cref="NestedMapMethod"/>, without <c>?</c>): the type argument
    /// of <c>NestedObjectIsNull&lt;T&gt;</c> for <see cref="Analysis.NullHandling.Throw"/>; <see langword="null"/> for
    /// other properties.
    /// </summary>
    public string? NestedSourceTypeFqn { get; }

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
    /// <param name="mappingPairs">See <see cref="MappingPairs"/>.</param>
    /// <param name="nested">The nested object mapping, or <see langword="null"/>.</param>
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
        bool converterIsCached = false,
        EquatableArray<string> mappingPairs = default,
        NestedObjectMapping? nested = null)
    {
        MappingPairs = mappingPairs;
        NestedMapMethod = nested?.Method;
        NullHandling = nested?.NullHandling;
        NestedUpdatesInPlace = nested?.UpdatesInPlace ?? false;
        NestedSourceTypeFqn = nested?.SourceTypeFqn;
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
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

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
            && CollectionDestElementFqn == other.CollectionDestElementFqn
            && MappingPairs.Equals(other.MappingPairs)
            && NestedMapMethod == other.NestedMapMethod
            && NullHandling == other.NullHandling
            && NestedUpdatesInPlace == other.NestedUpdatesInPlace
            && NestedSourceTypeFqn == other.NestedSourceTypeFqn;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PropertyMappingDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
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
        hash = hash * 31 + MappingPairs.GetHashCode();
        hash = hash * 31 + (NestedMapMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (NullHandling?.GetHashCode() ?? 0);
        hash = hash * 31 + NestedUpdatesInPlace.GetHashCode();
        hash = hash * 31 + (NestedSourceTypeFqn?.GetHashCode() ?? 0);
        return hash;
    }
}
