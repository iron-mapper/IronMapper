namespace IronMapper.Configuration;

/// <summary>Options of a profile mapping that are read by the source generator only.</summary>
public static class MappingExpressionExtensions
{
    /// <summary>
    /// Chooses which overloads the generator emits for this mapping. <see langword="null"/> (not passed): the value for
    /// the assembly applies (MSBuild properties <c>IronMapperGenerateInPlace</c>, <c>IronMapperGenerateList</c>,
    /// <c>IronMapperGenerateArray</c>, <see langword="true"/> by default). <c>MapToX(source)</c> is always generated.
    /// Applies to this mapping, not to the reverse mapping of <c>ReverseMap()</c>. The arguments must be constants
    /// (<see langword="true"/>, <see langword="false"/>, <see langword="null"/>): the generator reads them at compile time.
    /// </summary>
    /// <param name="expression">The mapping.</param>
    /// <param name="inPlace">
    /// The in-place overload <c>MapToX(source, destination)</c>. <see langword="false"/> removes only the public method:
    /// <c>IMapper.Map(source, destination)</c> keeps working through a private copy of its body.
    /// </param>
    /// <param name="list"><c>MapToXList(IEnumerable&lt;TSource&gt;?)</c>.</param>
    /// <param name="array"><c>MapToXArray(IEnumerable&lt;TSource&gt;?)</c>.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns><paramref name="expression"/>, for further chaining.</returns>
    public static IMappingExpression<TSource, TDest> GenerateMethods<TSource, TDest>(
        this IMappingExpression<TSource, TDest> expression,
        bool? inPlace = null,
        bool? list = null,
        bool? array = null)
        => expression;
}
