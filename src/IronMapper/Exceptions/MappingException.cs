using System;

namespace IronMapper.Exceptions;

/// <summary>
/// Base exception for all IronMapper runtime errors.
/// Under normal usage with the Source Generator this exception should never be thrown,
/// as mapping errors are surfaced as compile-time diagnostics.
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
