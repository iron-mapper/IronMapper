namespace IronMapper.Configuration;

/// <summary>
/// Describes one nested source member whose public properties are flattened
/// into the destination type via
/// <see cref="IMappingExpression{TSource,TDest}.IncludeMembers"/>.
/// Properties are matched by name to destination properties.
/// </summary>
public sealed class IncludedMemberDescriptor
{
    /// <summary>The simple member name on the source type, e.g. <c>"Contact"</c> or <c>"Address"</c>.</summary>
    public string MemberPath { get; }

    /// <summary>Initialises a new <see cref="IncludedMemberDescriptor"/>.</summary>
    /// <param name="memberPath">The simple member name on the source type.</param>
    public IncludedMemberDescriptor(string memberPath)
    {
        MemberPath = memberPath;
    }
}
