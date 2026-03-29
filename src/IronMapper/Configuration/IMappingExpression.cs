using System;
using System.Linq.Expressions;
using IronMapper.Interfaces;

namespace IronMapper.Configuration;

/// <summary>
/// Fluent builder for configuring the mapping between <typeparamref name="TSource"/>
/// and <typeparamref name="TDest"/>.
/// Obtained by calling <see cref="MappingProfile.CreateMap{TSource,TDest}"/>.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDest">The destination type.</typeparam>
public interface IMappingExpression<TSource, TDest>
{
    /// <summary>
    /// Configures how a specific destination member is populated.
    /// </summary>
    /// <param name="dest">Expression selecting the destination member to configure.</param>
    /// <param name="opts">A delegate that receives the member configuration builder.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> ForMember(
        Expression<Func<TDest, object?>> dest,
        Action<IMemberConfigurationExpression<TSource, TDest>> opts);

    /// <summary>
    /// Instructs the mapper to skip the destination member selected by <paramref name="dest"/>.
    /// </summary>
    /// <param name="dest">Expression selecting the destination member to ignore.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> Ignore(Expression<Func<TDest, object?>> dest);

    /// <summary>
    /// Applies the mapping only when <paramref name="condition"/> returns <see langword="true"/>.
    /// When the condition is not met the destination object is returned unchanged.
    /// </summary>
    /// <param name="condition">A predicate evaluated against the source object at mapping time.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> When(Func<TSource, bool> condition);

    /// <summary>
    /// Specifies that the entire mapping should be performed by a custom
    /// <see cref="ITypeConverter{TSource,TDest}"/> instance.
    /// </summary>
    /// <typeparam name="TConverter">
    /// A type implementing <see cref="ITypeConverter{TSource,TDest}"/>.
    /// </typeparam>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> ConvertUsing<TConverter>()
        where TConverter : ITypeConverter<TSource, TDest>;

    /// <summary>
    /// Also registers the reverse mapping from <typeparamref name="TDest"/> to
    /// <typeparamref name="TSource"/> with default member matching.
    /// </summary>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> ReverseMap();
}
