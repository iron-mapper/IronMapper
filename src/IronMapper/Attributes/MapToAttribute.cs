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
}
