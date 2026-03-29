namespace IronMapper.Attributes;

/// <summary>Marks a class as a mapping target for the specified source type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapToAttribute : Attribute
{
    public Type DestinationType { get; }

    public MapToAttribute(Type destinationType)
    {
        DestinationType = destinationType;
    }
}
