using System;
using System.Collections.Generic;
using IronMapper.Interfaces;

namespace IronMapper.Configuration;

/// <summary>
/// Base class for user-defined mapping profiles.
/// Inherit from this class and call <see cref="CreateMap{TSource,TDest}"/> in the constructor
/// to register mappings. The Source Generator will analyse derived classes at compile time.
/// </summary>
/// <example>
/// <code>
/// public class UserProfile : MappingProfile
/// {
///     public UserProfile()
///     {
///         CreateMap&lt;User, UserDto&gt;()
///             .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName));
///     }
/// }
/// </code>
/// </example>
public abstract class MappingProfile : IMappingProfile
{
    private readonly List<ValueTransformerDescriptor> _transformers = new();

    /// <summary>
    /// Registers a mapping from <typeparamref name="TSource"/> to <typeparamref name="TDest"/>
    /// and returns a fluent builder for further configuration.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns>
    /// An <see cref="IMappingExpression{TSource,TDest}"/> for configuring member mappings.
    /// </returns>
    protected IMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        return new MappingExpression<TSource, TDest>();
    }

    /// <summary>
    /// Registers a value transformer applied to every property of type <typeparamref name="TValue"/>
    /// across all mappings in this profile.
    /// </summary>
    /// <typeparam name="TValue">The type of values this transformer handles.</typeparam>
    /// <param name="transformer">A function that receives the source value and returns the transformed value.</param>
    protected void AddTransformer<TValue>(Func<TValue, TValue> transformer)
    {
        var methodName = $"TransformValue_{typeof(TValue).Name}";
        _transformers.Add(new ValueTransformerDescriptor(typeof(TValue), methodName, transformer));
    }

    /// <summary>Returns the list of registered value transformers for this profile.</summary>
    public IReadOnlyList<ValueTransformerDescriptor> GetTransformers() => _transformers;
}
