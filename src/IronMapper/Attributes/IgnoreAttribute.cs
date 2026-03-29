namespace IronMapper.Attributes;

/// <summary>
/// Instructs the Source Generator to skip this property or field during mapping.
/// The corresponding destination member will retain its default value.
/// </summary>
/// <example>
/// <code>
/// public class User
/// {
///     [Ignore]
///     public string InternalToken { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class IgnoreAttribute : Attribute
{
    /// <summary>Initializes a new instance of <see cref="IgnoreAttribute"/>.</summary>
    public IgnoreAttribute() { }
}
