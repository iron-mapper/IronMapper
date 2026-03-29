using System;
using System.Linq.Expressions;
using IronMapper.Interfaces;

namespace IronMapper.Configuration;

/// <summary>
/// Internal implementation of <see cref="IMappingExpression{TSource,TDest}"/>.
/// Stores configuration that is both analysed by the Source Generator at compile time
/// and available for any runtime fallback path.
/// </summary>
internal sealed class MappingExpression<TSource, TDest> : IMappingExpression<TSource, TDest>
{
    /// <inheritdoc/>
    public IMappingExpression<TSource, TDest> ForMember(
        Expression<Func<TDest, object?>> dest,
        Action<IMemberConfigurationExpression<TSource, TDest>> opts)
    {
        var memberConfig = new MemberConfigurationExpression<TSource, TDest>();
        opts(memberConfig);
        return this;
    }

    /// <inheritdoc/>
    public IMappingExpression<TSource, TDest> Ignore(Expression<Func<TDest, object?>> dest)
        => this;

    /// <inheritdoc/>
    public IMappingExpression<TSource, TDest> When(Func<TSource, bool> condition)
        => this;

    /// <inheritdoc/>
    public IMappingExpression<TSource, TDest> ConvertUsing<TConverter>()
        where TConverter : ITypeConverter<TSource, TDest>
        => this;

    /// <inheritdoc/>
    public IMappingExpression<TSource, TDest> ReverseMap()
        => this;
}
