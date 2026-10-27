using System;
using System.Linq.Expressions;

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

    /// <summary>
    /// Creates the destination with <paramref name="factory"/> instead of a constructor that the generator chooses: a
    /// static method group (<c>ConstructWith(Account.Restore)</c>, also when the constructor of the type is private) or a
    /// lambda with typed parameters (<c>ConstructWith((string name, int age) =&gt; new Person(name, age))</c>, which also
    /// chooses one of several constructors). Its parameters get their values like the parameters of a constructor: the
    /// source property of the same name (ignoring case), <c>ForCtorParam</c>, <c>ForMember</c> of the destination property of
    /// the same name, nested objects and collections through their mappings, the default value. The destination properties
    /// with a public <c>set</c> that no parameter sets are assigned after the call. An exception of the factory reaches the
    /// caller as it is. No in-place overload is generated for this mapping. The generator reads the argument at compile
    /// time: it must be a method group or a lambda written here (C# 10 or later; older versions:
    /// <c>new Func&lt;Guid, string, Account&gt;(Account.Restore)</c>).
    /// </summary>
    /// <param name="expression">The mapping.</param>
    /// <param name="factory">A static method or a lambda with typed parameters that returns <typeparamref name="TDest"/>.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns><paramref name="expression"/>, for further chaining.</returns>
    public static IMappingExpression<TSource, TDest> ConstructWith<TSource, TDest>(
        this IMappingExpression<TSource, TDest> expression,
        Delegate factory)
        => expression;

    /// <summary>
    /// Creates the destination with <paramref name="constructor"/>, written by hand as in AutoMapper: the generator emits
    /// the expression as it is and matches no parameters. The destination properties with a public <c>set</c> are then
    /// mapped as usual and assigned after it; <c>init</c> properties are left to the expression. An in-place update
    /// ignores it, as in AutoMapper (and is not generated for a destination without an accessible parameterless
    /// constructor).
    /// </summary>
    /// <param name="expression">The mapping.</param>
    /// <param name="constructor">Creates the destination from the source, e.g. <c>s =&gt; new OrderDto(s.Id)</c>.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns><paramref name="expression"/>, for further chaining.</returns>
    public static IMappingExpression<TSource, TDest> ConstructUsing<TSource, TDest>(
        this IMappingExpression<TSource, TDest> expression,
        Expression<Func<TSource, TDest>> constructor)
        => expression;

    /// <summary>
    /// Also registers the reverse mapping from <typeparamref name="TDest"/> to <typeparamref name="TSource"/>, converted
    /// by <paramref name="converter"/>: the short form of a second <c>CreateMap&lt;TDest, TSource&gt;().ConvertUsing(converter)</c>,
    /// for a pair of values such as <c>CreateMap&lt;Guid, UserId&gt;().ConvertUsing(UserId.From).ReverseMap(v =&gt; v.Value)</c>.
    /// Both mappings then convert every property, parameter and collection element of their pair. The generator reads the
    /// argument at compile time: a lambda or a static method group written here.
    /// </summary>
    /// <param name="expression">The mapping.</param>
    /// <param name="converter">Converts a <typeparamref name="TDest"/> back to a <typeparamref name="TSource"/>.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns><paramref name="expression"/>, for further chaining.</returns>
    public static IMappingExpression<TSource, TDest> ReverseMap<TSource, TDest>(
        this IMappingExpression<TSource, TDest> expression,
        Func<TDest, TSource> converter)
        => expression;

    /// <summary>
    /// Configures the parameter <paramref name="parameterName"/> of the constructor that creates the destination, or of
    /// the factory of <c>ConstructWith</c>: <c>MapFrom</c>, <c>UseConverter</c> or <c>Ignore()</c> (the default value). For
    /// a parameter without a destination property of the same name, which <c>ForMember</c> cannot select. The name is
    /// compared ignoring case and must be a constant (a literal or <c>nameof</c>); a name that is not a parameter is a
    /// diagnostic.
    /// </summary>
    /// <param name="expression">The mapping.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="options">The configuration of the parameter, e.g. <c>o =&gt; o.MapFrom(s =&gt; s.First + " " + s.Last)</c>.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns><paramref name="expression"/>, for further chaining.</returns>
    public static IMappingExpression<TSource, TDest> ForCtorParam<TSource, TDest>(
        this IMappingExpression<TSource, TDest> expression,
        string parameterName,
        Action<IMemberConfigurationExpression<TSource, TDest>> options)
    {
        options(new MemberConfigurationExpression<TSource, TDest>());
        return expression;
    }
}
