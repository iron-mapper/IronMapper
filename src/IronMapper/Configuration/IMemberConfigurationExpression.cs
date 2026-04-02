using System;
using System.Linq.Expressions;
using IronMapper.Interfaces;

namespace IronMapper.Configuration;

/// <summary>
/// Fluent builder for configuring how a single destination member is populated.
/// Obtained via <see cref="IMappingExpression{TSource,TDest}.ForMember"/>.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDest">The destination type.</typeparam>
public interface IMemberConfigurationExpression<TSource, TDest>
{
    /// <summary>
    /// Specifies which source member to read the value from using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TMember">The member value type.</typeparam>
    /// <param name="sourceMember">Lambda expression selecting the source property or field.</param>
    /// <returns>The current <see cref="IMemberConfigurationExpression{TSource,TDest}"/> for fluent chaining.</returns>
    IMemberConfigurationExpression<TSource, TDest> MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember);

    /// <summary>
    /// Specifies a custom resolver function to compute the destination member value.
    /// </summary>
    /// <param name="resolver">A function that receives the source object and returns the value.</param>
    /// <returns>The current <see cref="IMemberConfigurationExpression{TSource,TDest}"/> for fluent chaining.</returns>
    IMemberConfigurationExpression<TSource, TDest> MapFrom(Func<TSource, object?> resolver);

    /// <summary>
    /// Instructs the mapper to skip this destination member; it will retain its default value.
    /// </summary>
    /// <returns>The current <see cref="IMemberConfigurationExpression{TSource,TDest}"/> for fluent chaining.</returns>
    IMemberConfigurationExpression<TSource, TDest> Ignore();

    /// <summary>
    /// Specifies a custom <see cref="ITypeConverter"/> to convert the value for this member.
    /// </summary>
    /// <typeparam name="TConverter">
    /// A type implementing <see cref="ITypeConverter"/> for the source and destination member types.
    /// </typeparam>
    /// <returns>The current <see cref="IMemberConfigurationExpression{TSource,TDest}"/> for fluent chaining.</returns>
    IMemberConfigurationExpression<TSource, TDest> UseConverter<TConverter>() where TConverter : ITypeConverter;
}
