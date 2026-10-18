namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// Parts of a destination type used to build a qualified method name when <c>MapTo{Name}</c> collides
/// (see <see cref="MappingPlanner"/>).
/// </summary>
internal readonly struct DestinationNaming
{
    /// <summary>Namespace segments and containing types, outermost first, joined with '.'; empty for the global namespace.</summary>
    public string Qualifier { get; }

    /// <summary><c>Of</c> + type arguments by simple names (e.g. <c>OfInt32AndString</c>); empty when not generic.</summary>
    public string TypeArgsShort { get; }

    /// <summary><c>Of</c> + type arguments by full names (e.g. <c>OfSystem_Int32</c>); empty when not generic.</summary>
    public string TypeArgsFull { get; }

    /// <summary>Initialises a new <see cref="DestinationNaming"/>.</summary>
    public DestinationNaming(string qualifier, string typeArgsShort, string typeArgsFull)
    {
        Qualifier = qualifier;
        TypeArgsShort = typeArgsShort;
        TypeArgsFull = typeArgsFull;
    }
}
