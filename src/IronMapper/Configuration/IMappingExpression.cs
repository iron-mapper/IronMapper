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
    /// <example>
    /// <code>
    /// CreateMap&lt;User, UserDto&gt;()
    ///     .ForMember(d => d.FullName,
    ///                o => o.MapFrom(s => s.FirstName + " " + s.LastName));
    /// </code>
    /// </example>
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
    /// Specifies that the entire mapping is delegated to a custom conversion function.
    /// The generator extracts the lambda body and emits it verbatim as the method return expression.
    /// </summary>
    /// <param name="converter">A function that converts a <typeparamref name="TSource"/> to <typeparamref name="TDest"/>.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> ConvertUsing(Func<TSource, TDest> converter);

    /// <summary>
    /// Also registers the reverse mapping from <typeparamref name="TDest"/> to
    /// <typeparamref name="TSource"/> with default member matching.
    /// </summary>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    IMappingExpression<TSource, TDest> ReverseMap();

    /// <summary>
    /// Registers an action to be called immediately after the destination object is created
    /// and before any properties are assigned. The destination object is empty at this point.
    /// The generator extracts the lambda body and emits it as a private helper method.
    /// </summary>
    /// <param name="action">An action receiving the source and the (empty) destination objects.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// CreateMap&lt;UserEntity, UserDto&gt;()
    ///     .BeforeMap((src, dest) => Console.WriteLine($"Mapping {src.Id}"));
    /// </code>
    /// </example>
    IMappingExpression<TSource, TDest> BeforeMap(Action<TSource, TDest> action);

    /// <summary>
    /// Registers an action to be called after all properties have been assigned to the destination.
    /// The generator extracts the lambda body and emits it as a private helper method.
    /// </summary>
    /// <param name="action">An action receiving the source and the fully-populated destination objects.</param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// CreateMap&lt;UserEntity, UserDto&gt;()
    ///     .AfterMap((src, dest) => dest.MappedAt = DateTime.UtcNow);
    /// </code>
    /// </example>
    IMappingExpression<TSource, TDest> AfterMap(Action<TSource, TDest> action);

    /// <summary>
    /// Flattens one or more nested source objects into the destination type by matching
    /// their property names against destination property names.
    /// The first listed member wins when multiple included members share a property name.
    /// Direct source properties and explicit <see cref="ForMember"/> configurations
    /// always take priority over <see cref="IncludeMembers"/> matches.
    /// The source generator analyses the lambda expressions at compile time
    /// and emits <c>source.Member.Property</c> access expressions for each matched property.
    /// </summary>
    /// <param name="memberExpressions">
    /// Lambdas of the form <c>s =&gt; s.Contact</c> identifying the nested members to flatten.
    /// </param>
    /// <returns>This <see cref="IMappingExpression{TSource,TDest}"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// CreateMap&lt;UserEntity, UserDto&gt;()
    ///     .IncludeMembers(s =&gt; s.Contact, s =&gt; s.Address);
    /// </code>
    /// </example>
    IMappingExpression<TSource, TDest> IncludeMembers(
        params Expression<Func<TSource, object?>>[] memberExpressions);
}
