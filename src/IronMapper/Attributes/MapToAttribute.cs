namespace IronMapper.Attributes;

/// <summary>
/// Marks a class as a mapping source and specifies the destination type.
/// The Source Generator will emit a mapping method from the decorated type to <see cref="DestinationType"/>.
/// </summary>
/// <example>
/// <code>
/// [MapTo(typeof(UserDto))]
/// public class User { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class MapToAttribute : Attribute
{
    /// <summary>The destination type that this type should be mapped to.</summary>
    public Type DestinationType { get; }

    /// <summary>Initializes a new instance of <see cref="MapToAttribute"/>.</summary>
    /// <param name="destinationType">The type to map to.</param>
    public MapToAttribute(Type destinationType)
    {
        DestinationType = destinationType;
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
