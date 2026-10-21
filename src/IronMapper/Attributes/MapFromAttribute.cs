namespace IronMapper.Attributes;

/// <summary>
/// Marks a class as a mapping destination and specifies the source type.
/// The Source Generator will emit a mapping method from <see cref="SourceType"/> to the decorated type.
/// </summary>
/// <example>
/// <code>
/// [MapFrom(typeof(User))]
/// public class UserDto { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class MapFromAttribute : Attribute
{
    /// <summary>The source type that should be mapped from.</summary>
    public Type SourceType { get; }

    /// <summary>Initializes a new instance of <see cref="MapFromAttribute"/>.</summary>
    /// <param name="sourceType">The type to map from.</param>
    public MapFromAttribute(Type sourceType)
    {
        SourceType = sourceType;
    }

    /// <summary>
    /// Whether the generator emits the in-place overload <c>MapToX(source, destination)</c> for this mapping. Not set: the value for the assembly applies
    /// (MSBuild property <c>IronMapperGenerateInPlace</c>, <see langword="true"/> by default); <see langword="true"/> or
    /// <see langword="false"/> overrides it for this mapping only. The getter returns <see langword="false"/> when the
    /// property is not set: that is a limitation of C# attributes (an attribute property cannot be "not set"), not the
    /// behavior: the generator reads whether the property is set in the attribute usage.
    /// Turning it off removes only the public method: <c>IMapper.Map(source, destination)</c> keeps working through a
    /// private copy of its body.
    /// </summary>
    public bool GenerateInPlace { get; set; }

    /// <summary>
    /// Whether the generator emits <c>MapToXList(IEnumerable&lt;TSource&gt;?)</c> for this mapping. Not set: the value for the assembly applies
    /// (MSBuild property <c>IronMapperGenerateList</c>, <see langword="true"/> by default); <see langword="true"/> or
    /// <see langword="false"/> overrides it for this mapping only. The getter returns <see langword="false"/> when the
    /// property is not set: that is a limitation of C# attributes (an attribute property cannot be "not set"), not the
    /// behavior: the generator reads whether the property is set in the attribute usage.
    /// </summary>
    public bool GenerateList { get; set; }

    /// <summary>
    /// Whether the generator emits <c>MapToXArray(IEnumerable&lt;TSource&gt;?)</c> for this mapping. Not set: the value for the assembly applies
    /// (MSBuild property <c>IronMapperGenerateArray</c>, <see langword="true"/> by default); <see langword="true"/> or
    /// <see langword="false"/> overrides it for this mapping only. The getter returns <see langword="false"/> when the
    /// property is not set: that is a limitation of C# attributes (an attribute property cannot be "not set"), not the
    /// behavior: the generator reads whether the property is set in the attribute usage.
    /// </summary>
    public bool GenerateArray { get; set; }
}
