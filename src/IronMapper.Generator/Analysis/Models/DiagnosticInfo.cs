using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// A serialisable representation of a diagnostic to report.
/// Stored inside <see cref="MappingDescriptor"/> so the incremental pipeline
/// can cache it without holding live <see cref="Location"/> references.
/// </summary>
internal readonly struct DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    /// <summary>The Roslyn <see cref="DiagnosticDescriptor"/> that describes the rule being violated.</summary>
    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>Format arguments to interpolate into <see cref="DiagnosticDescriptor.MessageFormat"/>.</summary>
    public EquatableArray<string> MessageArgs { get; }

    /// <summary>Initialises a new <see cref="DiagnosticInfo"/>.</summary>
    /// <param name="descriptor">The diagnostic rule descriptor.</param>
    /// <param name="messageArgs">Arguments for the message format string.</param>
    public DiagnosticInfo(DiagnosticDescriptor descriptor, params string[] messageArgs)
    {
        Descriptor = descriptor;
        MessageArgs = ImmutableArray.Create(messageArgs);
    }

    /// <summary>Creates the diagnostic to report (without a source location).</summary>
    public Diagnostic ToDiagnostic()
    {
        var args = new object[MessageArgs.Length];
        for (var i = 0; i < args.Length; i++) args[i] = MessageArgs[i];
        return Diagnostic.Create(Descriptor, Location.None, args);
    }

    /// <inheritdoc/>
    public bool Equals(DiagnosticInfo other)
        => Equals(Descriptor, other.Descriptor) && MessageArgs.Equals(other.MessageArgs);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = Descriptor?.Id.GetHashCode() ?? 0;
        return hash * 31 + MessageArgs.GetHashCode();
    }
}
