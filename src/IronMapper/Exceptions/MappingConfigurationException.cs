using System;

namespace IronMapper.Exceptions;

/// <summary>
/// Thrown when a mapping configuration is invalid or a requested mapping has not been registered.
/// This typically indicates that the Source Generator did not run (e.g. the generator assembly is
/// not referenced), or an unsupported runtime mapping was attempted.
/// </summary>
public sealed class MappingConfigurationException : MappingException
{
    /// <summary>The source type involved in the failed mapping, if known.</summary>
    public Type? SourceType { get; }

    /// <summary>The destination type involved in the failed mapping, if known.</summary>
    public Type? DestinationType { get; }

    /// <summary>Initializes a new instance of <see cref="MappingConfigurationException"/>.</summary>
    public MappingConfigurationException() { }

    /// <summary>
    /// Initializes a new instance of <see cref="MappingConfigurationException"/> with a message.
    /// </summary>
    /// <param name="message">A description of the configuration error.</param>
    public MappingConfigurationException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of <see cref="MappingConfigurationException"/> with source/dest types.
    /// </summary>
    /// <param name="sourceType">The source type for which no mapping was found.</param>
    /// <param name="destinationType">The expected destination type.</param>
    public MappingConfigurationException(Type sourceType, Type destinationType)
        : base($"No mapping registered from '{sourceType.FullName}' to '{destinationType.FullName}'. " +
               "Ensure the IronMapper.Generator is referenced and the types are annotated with [MapTo] or [MapFrom].")
    {
        SourceType = sourceType;
        DestinationType = destinationType;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MappingConfigurationException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public MappingConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
