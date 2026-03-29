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
}
