namespace IronMapper.Attributes;

/// <summary>
/// Specifies a custom <see cref="Interfaces.ITypeConverter{TSource,TDest}"/> to use
/// when mapping this property. The converter type must implement
/// <see cref="Interfaces.ITypeConverter{TSource,TDest}"/> for the appropriate member types.
/// </summary>
/// <example>
/// <code>
/// public class Order
/// {
///     [MapConverter(typeof(DateToStringConverter))]
///     public DateTime CreatedAt { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MapConverterAttribute : Attribute
{
    /// <summary>The converter type to use for this member.</summary>
    public Type ConverterType { get; }

    /// <summary>Initializes a new instance of <see cref="MapConverterAttribute"/>.</summary>
    /// <param name="converterType">
    /// A type implementing <see cref="Interfaces.ITypeConverter{TSource,TDest}"/>.
    /// </param>
    public MapConverterAttribute(Type converterType)
    {
        ConverterType = converterType;
    }
}
