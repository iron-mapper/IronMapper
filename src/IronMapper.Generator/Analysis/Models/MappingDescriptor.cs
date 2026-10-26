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
    public EquatableArray<PropertyMappingDescriptor> PropertyMappings { get; }

    /// <summary>Diagnostics to report when this mapping is emitted.</summary>
    public EquatableArray<DiagnosticInfo> Diagnostics { get; }

    /// <summary>True when at least one property uses a custom ITypeConverter.</summary>
    public bool HasCustomConverter { get; }

    /// <summary>
    /// When non-null, the generated mapper wraps the entire mapping in a condition.
    /// This is a verbatim C# expression (using "source" as the source variable) emitted as:
    /// <code>if (!(conditionBody)) return default!;</code>
    /// Produced by When(src =&gt; ...) in a MappingProfile.
    /// </summary>
    public string? WhenConditionBody { get; }

    /// <summary>
    /// When non-null, the entire mapping is delegated to a custom <c>ITypeConverter</c> implementation.
    /// This is the fully-qualified type name (e.g. <c>"MyApp.Converters.OrderConverter"</c>).
    /// Produced by <c>ConvertUsing&lt;TConverter&gt;()</c> in a MappingProfile.
    /// </summary>
    public string? WholeObjectConverterType { get; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="WholeObjectConverterType"/> has no state and the generated code reuses one
    /// instance (<c>ConverterCache&lt;T&gt;.Instance</c>); otherwise it creates one with <c>new</c> on every call.
    /// </summary>
    public bool WholeObjectConverterIsCached { get; }

    /// <summary>
    /// When non-null, the generated method body is this verbatim C# expression (using "source").
    /// Produced by <c>ConvertUsing(src =&gt; ...)</c> in a MappingProfile.
    /// </summary>
    public string? WholeObjectLambdaBody { get; }

    /// <summary>
    /// When non-null, the generator emits a private helper called before properties are assigned.
    /// This is the verbatim C# body of the <c>BeforeMap((source, destination) =&gt; ...)</c> lambda,
    /// with the lambda parameters renamed to <c>source</c> and <c>destination</c>.
    /// </summary>
    public string? BeforeMapLambdaBody { get; }

    /// <summary>
    /// When non-null, the generator emits a private helper called after all properties are assigned.
    /// This is the verbatim C# body of the <c>AfterMap((source, destination) =&gt; ...)</c> lambda,
    /// with the lambda parameters renamed to <c>source</c> and <c>destination</c>.
    /// </summary>
    public string? AfterMapLambdaBody { get; }

    /// <summary>Profile-level value transformers to apply to name-matched properties by type.</summary>
    public EquatableArray<ValueTransformerDescriptor> ValueTransformers { get; }

    /// <summary>
    /// Fully-qualified C# name of the source type (e.g. <c>global::App.Outer.Inner</c>), or <see langword="null"/>
    /// to derive it from <see cref="SourceNamespace"/> and <see cref="SourceTypeName"/>.
    /// </summary>
    public string? SourceFullName { get; }

    /// <summary>
    /// Fully-qualified C# name of the destination type, or <see langword="null"/>
    /// to derive it from <see cref="DestNamespace"/> and <see cref="DestTypeName"/>.
    /// </summary>
    public string? DestFullName { get; }

    /// <summary>
    /// Accessibility modifier (<c>"public"</c> or <c>"internal"</c>) of the generated non-private methods:
    /// the minimum effective accessibility of the source and destination types.
    /// </summary>
    public string MethodAccessibility { get; }

    /// <summary>
    /// Stable key of the definition site (e.g. <c>attr:MapTo:...</c>, <c>profile:App.UserProfile#0000.0002</c>).
    /// Distinguishes several definitions of the same type pair; empty for diagnostic-only descriptors.
    /// </summary>
    public string Origin { get; }

    /// <summary>Human-readable definition site for diagnostics, e.g. <c>MappingProfile 'App.UserProfile'</c>.</summary>
    public string OriginDisplay { get; }

    /// <summary>
    /// Precedence when the same type pair is defined more than once (lower wins):
    /// 0 = <c>CreateMap</c>, 1 = <c>ReverseMap()</c>, 2 = <c>[MapTo]</c>, 3 = <c>[MapFrom]</c>.
    /// </summary>
    public int OriginRank { get; }

    /// <summary>
    /// Namespace segments and containing types of the destination, outermost first, joined with '.'
    /// (e.g. <c>App.Admin</c>, or <c>App.Orders</c> for a type nested in <c>App.Orders</c>); empty for the
    /// global namespace. Used only to qualify colliding method names.
    /// </summary>
    public string DestQualifier { get; }

    /// <summary>Type-argument part of a qualified method name by simple names (e.g. <c>OfInt32</c>); empty for non-generic types.</summary>
    public string DestTypeArgsShort { get; }

    /// <summary>Type-argument part of a qualified method name by full names (e.g. <c>OfSystem_Int32</c>); empty for non-generic types.</summary>
    public string DestTypeArgsFull { get; }

    /// <summary>
    /// Where the mapping is defined (the <c>[MapTo]</c>/<c>[MapFrom]</c> attribute, the <c>CreateMap</c> call or the
    /// <c>ReverseMap()</c> call); the plan's diagnostics (IM0007, IM0013, IM0014) point here. Not part of the copy used to
    /// generate code (<see cref="WithoutDiagnostics"/>), so moving the definition does not regenerate it.
    /// </summary>
    public LocationInfo? OriginLocation { get; }

    /// <summary>
    /// Overloads set for this mapping (<c>GenerateInPlace</c>/<c>GenerateList</c>/<c>GenerateArray</c>,
    /// <c>GenerateMethods(...)</c>); unset values come from the assembly (<see cref="GeneratorSettings.Methods"/>).
    /// </summary>
    public GeneratedMethodOverrides MethodOverrides { get; }

    /// <summary>
    /// The generated code of this mapping uses <c>ConverterCache&lt;T&gt;</c> (declared once in <c>GeneratedMappers.g.cs</c>).
    /// </summary>
    public bool UsesConverterCache
    {
        get
        {
            if (WholeObjectConverterIsCached)
            {
                return true;
            }

            foreach (var property in PropertyMappings)
            {
                if (property.ConverterIsCached)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The in-place <c>…Core</c> of this mapping writes every property that a new object gets, so the in-place overload
    /// of a mapping that has this pair as a nested object may update an existing nested object. <see langword="false"/>
    /// with <c>ConvertUsing</c> (the converter makes the object) or a mapped <c>init</c>-only property (the in-place
    /// overload skips it, item 2.15 of the plan): such a nested object is assigned a new instance, as before.
    /// </summary>
    public bool CanUpdateInPlace
    {
        get
        {
            if (WholeObjectConverterType is not null || WholeObjectLambdaBody is not null)
            {
                return false;
            }

            foreach (var property in PropertyMappings)
            {
                if (property.IsInitOnly && !property.IsIgnored)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Key of the type pair: <c>{SourceFullName}-&gt;{DestFullName}</c>.</summary>
    public string PairKey => $"{SourceFullName}->{DestFullName}";

    /// <summary>Initialises a new <see cref="MappingDescriptor"/>.</summary>
    /// <param name="sourceTypeName">Simple (unqualified) name of the source type.</param>
    /// <param name="sourceNamespace">Namespace of the source type, or <see langword="null"/> for the global namespace.</param>
    /// <param name="destTypeName">Simple (unqualified) name of the destination type.</param>
    /// <param name="destNamespace">Namespace of the destination type, or <see langword="null"/> for the global namespace.</param>
    /// <param name="propertyMappings">Per-property mapping instructions in declaration order.</param>
    /// <param name="diagnostics">Diagnostics to surface when this descriptor is emitted.</param>
    /// <param name="hasCustomConverter">Whether any property mapping uses a custom <c>ITypeConverter</c>.</param>
    /// <param name="whenConditionBody">Optional verbatim C# condition expression (uses "source") emitted as a guard before mapping.</param>
    /// <param name="wholeObjectConverterType">Fully-qualified name of an <c>ITypeConverter</c> that converts the entire source object, or <see langword="null"/>.</param>
    /// <param name="wholeObjectLambdaBody">Verbatim C# expression that returns the entire destination object, or <see langword="null"/>.</param>
    /// <param name="beforeMapLambdaBody">Verbatim C# statements for the BeforeMap hook, or <see langword="null"/>.</param>
    /// <param name="afterMapLambdaBody">Verbatim C# statements for the AfterMap hook, or <see langword="null"/>.</param>
    /// <param name="valueTransformers">Profile-level value transformers, or default.</param>
    /// <param name="sourceFullName">Fully-qualified source type name, or <see langword="null"/> to derive it.</param>
    /// <param name="destFullName">Fully-qualified destination type name, or <see langword="null"/> to derive it.</param>
    /// <param name="methodAccessibility">Accessibility modifier of the generated methods (<c>"public"</c> or <c>"internal"</c>).</param>
    /// <param name="origin">Stable key of the definition site (see <see cref="Origin"/>).</param>
    /// <param name="originDisplay">Human-readable definition site for diagnostics.</param>
    /// <param name="originRank">Precedence among duplicate definitions (see <see cref="OriginRank"/>).</param>
    /// <param name="destQualifier">Namespace and containing types of the destination (see <see cref="DestQualifier"/>).</param>
    /// <param name="destTypeArgsShort">Type-argument name part by simple names (see <see cref="DestTypeArgsShort"/>).</param>
    /// <param name="destTypeArgsFull">Type-argument name part by full names (see <see cref="DestTypeArgsFull"/>).</param>
    /// <param name="wholeObjectConverterIsCached">Whether one instance of <paramref name="wholeObjectConverterType"/> is reused.</param>
    /// <param name="originLocation">Where the mapping is defined (see <see cref="OriginLocation"/>).</param>
    /// <param name="methodOverrides">Overloads set for this mapping (see <see cref="MethodOverrides"/>).</param>
    public MappingDescriptor(
        string sourceTypeName,
        string? sourceNamespace,
        string destTypeName,
        string? destNamespace,
        EquatableArray<PropertyMappingDescriptor> propertyMappings,
        EquatableArray<DiagnosticInfo> diagnostics,
        bool hasCustomConverter,
        string? whenConditionBody = null,
        string? wholeObjectConverterType = null,
        string? wholeObjectLambdaBody = null,
        string? beforeMapLambdaBody = null,
        string? afterMapLambdaBody = null,
        EquatableArray<ValueTransformerDescriptor> valueTransformers = default,
        string? sourceFullName = null,
        string? destFullName = null,
        string methodAccessibility = "public",
        string origin = "",
        string originDisplay = "",
        int originRank = 0,
        string destQualifier = "",
        string destTypeArgsShort = "",
        string destTypeArgsFull = "",
        bool wholeObjectConverterIsCached = false,
        LocationInfo? originLocation = null,
        GeneratedMethodOverrides methodOverrides = default)
    {
        SourceTypeName = sourceTypeName;
        SourceNamespace = sourceNamespace;
        DestTypeName = destTypeName;
        DestNamespace = destNamespace;
        PropertyMappings = propertyMappings;
        Diagnostics = diagnostics;
        HasCustomConverter = hasCustomConverter;
        WhenConditionBody = whenConditionBody;
        WholeObjectConverterType = wholeObjectConverterType;
        WholeObjectConverterIsCached = wholeObjectConverterIsCached;
        OriginLocation = originLocation;
        MethodOverrides = methodOverrides;
        WholeObjectLambdaBody = wholeObjectLambdaBody;
        BeforeMapLambdaBody = beforeMapLambdaBody;
        AfterMapLambdaBody = afterMapLambdaBody;
        ValueTransformers = valueTransformers;
        SourceFullName = sourceFullName;
        DestFullName = destFullName;
        MethodAccessibility = methodAccessibility;
        Origin = origin;
        OriginDisplay = originDisplay;
        OriginRank = originRank;
        DestQualifier = destQualifier;
        DestTypeArgsShort = destTypeArgsShort;
        DestTypeArgsFull = destTypeArgsFull;
    }

    /// <summary>
    /// Returns a copy with the definition site (see <see cref="Origin"/>) and the destination naming parts
    /// (see <see cref="DestQualifier"/>) set. Diagnostic-only descriptors are returned unchanged.
    /// </summary>
    public MappingDescriptor WithOrigin(string origin, string originDisplay, int originRank, DestinationNaming naming,
        LocationInfo? originLocation)
        => SourceTypeName.Length == 0
            ? this
            : new(SourceTypeName, SourceNamespace, DestTypeName, DestNamespace, PropertyMappings, Diagnostics,
                HasCustomConverter, WhenConditionBody, WholeObjectConverterType, WholeObjectLambdaBody,
                BeforeMapLambdaBody, AfterMapLambdaBody, ValueTransformers, SourceFullName, DestFullName,
                MethodAccessibility, origin, originDisplay, originRank,
                naming.Qualifier, naming.TypeArgsShort, naming.TypeArgsFull, WholeObjectConverterIsCached, originLocation,
                MethodOverrides);

    /// <summary>A copy with the overloads set for this mapping (see <see cref="MethodOverrides"/>).</summary>
    public MappingDescriptor WithMethodOverrides(GeneratedMethodOverrides overrides)
        => SourceTypeName.Length == 0 || overrides.Equals(MethodOverrides)
            ? this
            : new(SourceTypeName, SourceNamespace, DestTypeName, DestNamespace, PropertyMappings, Diagnostics,
                HasCustomConverter, WhenConditionBody, WholeObjectConverterType, WholeObjectLambdaBody,
                BeforeMapLambdaBody, AfterMapLambdaBody, ValueTransformers, SourceFullName, DestFullName,
                MethodAccessibility, Origin, OriginDisplay, OriginRank, DestQualifier, DestTypeArgsShort,
                DestTypeArgsFull, WholeObjectConverterIsCached, OriginLocation, overrides);

    /// <summary>
    /// The copy used to generate code: without <see cref="Diagnostics"/> and <see cref="OriginLocation"/>. Diagnostics
    /// carry locations, which change with every line added above them; the generated code does not depend on them, so
    /// this copy stays equal and the mapping's file stays cached. Diagnostics are reported by a separate output.
    /// </summary>
    public MappingDescriptor WithoutDiagnostics()
        => Diagnostics.Length == 0 && OriginLocation is null
            ? this
            : new(SourceTypeName, SourceNamespace, DestTypeName, DestNamespace, PropertyMappings,
                EquatableArray<DiagnosticInfo>.Empty, HasCustomConverter, WhenConditionBody, WholeObjectConverterType,
                WholeObjectLambdaBody, BeforeMapLambdaBody, AfterMapLambdaBody, ValueTransformers, SourceFullName,
                DestFullName, MethodAccessibility, Origin, OriginDisplay, OriginRank, DestQualifier, DestTypeArgsShort,
                DestTypeArgsFull, WholeObjectConverterIsCached, methodOverrides: MethodOverrides);

    /// <inheritdoc/>
    public bool Equals(MappingDescriptor? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (SourceTypeName != other.SourceTypeName
            || SourceNamespace != other.SourceNamespace
            || DestTypeName != other.DestTypeName
            || DestNamespace != other.DestNamespace
            || HasCustomConverter != other.HasCustomConverter
            || WhenConditionBody != other.WhenConditionBody
            || WholeObjectConverterType != other.WholeObjectConverterType
            || WholeObjectConverterIsCached != other.WholeObjectConverterIsCached
            || WholeObjectLambdaBody != other.WholeObjectLambdaBody
            || BeforeMapLambdaBody != other.BeforeMapLambdaBody
            || AfterMapLambdaBody != other.AfterMapLambdaBody
            || SourceFullName != other.SourceFullName
            || DestFullName != other.DestFullName
            || MethodAccessibility != other.MethodAccessibility
            || Origin != other.Origin
            || OriginDisplay != other.OriginDisplay
            || OriginRank != other.OriginRank
            || DestQualifier != other.DestQualifier
            || DestTypeArgsShort != other.DestTypeArgsShort
            || DestTypeArgsFull != other.DestTypeArgsFull
            || !Nullable.Equals(OriginLocation, other.OriginLocation)
            || !MethodOverrides.Equals(other.MethodOverrides))
        {
            return false;
        }

        // Diagnostics count too: an edit that only adds or removes a diagnostic must reach the output.
        return PropertyMappings.Equals(other.PropertyMappings)
            && Diagnostics.Equals(other.Diagnostics)
            && ValueTransformers.Equals(other.ValueTransformers);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + SourceTypeName.GetHashCode();
        hash = hash * 31 + (SourceNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + DestTypeName.GetHashCode();
        hash = hash * 31 + (DestNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + HasCustomConverter.GetHashCode();
        hash = hash * 31 + (WhenConditionBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (WholeObjectConverterType?.GetHashCode() ?? 0);
        hash = hash * 31 + WholeObjectConverterIsCached.GetHashCode();
        hash = hash * 31 + (WholeObjectLambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (BeforeMapLambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (AfterMapLambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (SourceFullName?.GetHashCode() ?? 0);
        hash = hash * 31 + (DestFullName?.GetHashCode() ?? 0);
        hash = hash * 31 + MethodAccessibility.GetHashCode();
        hash = hash * 31 + Origin.GetHashCode();
        hash = hash * 31 + OriginDisplay.GetHashCode();
        hash = hash * 31 + OriginRank;
        hash = hash * 31 + DestQualifier.GetHashCode();
        hash = hash * 31 + DestTypeArgsShort.GetHashCode();
        hash = hash * 31 + DestTypeArgsFull.GetHashCode();
        hash = hash * 31 + OriginLocation.GetHashCode();
        hash = hash * 31 + MethodOverrides.GetHashCode();
        hash = hash * 31 + PropertyMappings.GetHashCode();
        hash = hash * 31 + Diagnostics.GetHashCode();
        hash = hash * 31 + ValueTransformers.GetHashCode();
        return hash;
    }
}
