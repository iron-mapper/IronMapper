using System;

namespace IronMapper.Exceptions;

/// <summary>
/// Base exception for all IronMapper runtime errors.
/// Thrown, for example, by the <c>IMapper</c> implementation in <c>IronMapper.Extensions.DI</c>
/// when no generated mapping exists for the requested type pair, and by <c>MappingValidator.ValidateAndThrow</c>.
/// </summary>
public class MappingException : Exception
{
    /// <summary>Initializes a new instance of <see cref="MappingException"/>.</summary>
    public MappingException() { }

    /// <summary>Initializes a new instance of <see cref="MappingException"/> with a message.</summary>
    /// <param name="message">A description of the error.</param>
    public MappingException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of <see cref="MappingException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public MappingException(string message, Exception innerException)
        : base(message, innerException) { }
}
