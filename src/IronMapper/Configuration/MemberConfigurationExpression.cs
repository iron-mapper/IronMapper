using System;
using System.Linq.Expressions;
using IronMapper.Interfaces;

namespace IronMapper.Configuration;

/// <summary>
/// Internal implementation of <see cref="IMemberConfigurationExpression{TSource,TDest}"/>.
/// </summary>
internal sealed class MemberConfigurationExpression<TSource, TDest>
    : IMemberConfigurationExpression<TSource, TDest>
{
    /// <inheritdoc/>
    public IMemberConfigurationExpression<TSource, TDest> MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember) => this;

    /// <inheritdoc/>
    public IMemberConfigurationExpression<TSource, TDest> MapFrom(Func<TSource, object?> resolver) => this;

    /// <inheritdoc/>
    public IMemberConfigurationExpression<TSource, TDest> Ignore() => this;

    /// <inheritdoc/>
    public IMemberConfigurationExpression<TSource, TDest> UseConverter<TConverter>() where TConverter : ITypeConverter => this;
}
