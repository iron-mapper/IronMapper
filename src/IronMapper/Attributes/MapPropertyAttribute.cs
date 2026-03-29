namespace IronMapper.Attributes;

/// <summary>
/// Maps this property to a differently-named property on the destination type.
/// When applied to a source property, the value will be assigned to
/// <see cref="DestinationProperty"/> instead of a property with the same name.
/// </summary>
/// <example>
/// <code>
/// public class User
/// {
///     [MapProperty("FullName")]
///     public string Name { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>The name of the destination property to map this member to.</summary>
    public string DestinationProperty { get; }

    /// <summary>Initializes a new instance of <see cref="MapPropertyAttribute"/>.</summary>
    /// <param name="destinationProperty">The name of the destination property.</param>
    public MapPropertyAttribute(string destinationProperty)
    {
        DestinationProperty = destinationProperty;
    }
}
