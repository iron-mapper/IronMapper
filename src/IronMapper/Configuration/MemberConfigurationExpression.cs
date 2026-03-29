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
    public void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember) { }

    /// <inheritdoc/>
    public void MapFrom(Func<TSource, object?> resolver) { }

    /// <inheritdoc/>
    public void Ignore() { }

    /// <inheritdoc/>
    public void UseConverter<TConverter>() where TConverter : ITypeConverter { }
}
