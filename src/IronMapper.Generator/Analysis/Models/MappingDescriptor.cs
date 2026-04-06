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
    public MappingDescriptor(
        string sourceTypeName,
        string? sourceNamespace,
        string destTypeName,
        string? destNamespace,
        ImmutableArray<PropertyMappingDescriptor> propertyMappings,
        ImmutableArray<DiagnosticInfo> diagnostics,
        bool hasCustomConverter,
        string? whenConditionBody = null,
        string? wholeObjectConverterType = null,
        string? wholeObjectLambdaBody = null,
        string? beforeMapLambdaBody = null,
        string? afterMapLambdaBody = null)
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
        WholeObjectLambdaBody = wholeObjectLambdaBody;
        BeforeMapLambdaBody = beforeMapLambdaBody;
        AfterMapLambdaBody = afterMapLambdaBody;
    }

    /// <inheritdoc/>
    public bool Equals(MappingDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (SourceTypeName != other.SourceTypeName
            || SourceNamespace != other.SourceNamespace
            || DestTypeName != other.DestTypeName
            || DestNamespace != other.DestNamespace
            || HasCustomConverter != other.HasCustomConverter
            || WhenConditionBody != other.WhenConditionBody
            || WholeObjectConverterType != other.WholeObjectConverterType
            || WholeObjectLambdaBody != other.WholeObjectLambdaBody
            || BeforeMapLambdaBody != other.BeforeMapLambdaBody
            || AfterMapLambdaBody != other.AfterMapLambdaBody)
            return false;

        if (PropertyMappings.Length != other.PropertyMappings.Length) return false;
        for (var i = 0; i < PropertyMappings.Length; i++)
            if (!PropertyMappings[i].Equals(other.PropertyMappings[i])) return false;

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + SourceTypeName.GetHashCode();
        hash = hash * 31 + (SourceNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + DestTypeName.GetHashCode();
        hash = hash * 31 + (DestNamespace?.GetHashCode() ?? 0);
        hash = hash * 31 + HasCustomConverter.GetHashCode();
        hash = hash * 31 + (WhenConditionBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (WholeObjectConverterType?.GetHashCode() ?? 0);
        hash = hash * 31 + (WholeObjectLambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (BeforeMapLambdaBody?.GetHashCode() ?? 0);
        hash = hash * 31 + (AfterMapLambdaBody?.GetHashCode() ?? 0);
        foreach (var pm in PropertyMappings)
            hash = hash * 31 + pm.GetHashCode();
        return hash;
    }
}
