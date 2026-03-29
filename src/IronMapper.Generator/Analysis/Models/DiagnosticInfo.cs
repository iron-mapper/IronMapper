using System;
using System.Linq;
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
    public string[] MessageArgs { get; }

    /// <summary>Initialises a new <see cref="DiagnosticInfo"/>.</summary>
    /// <param name="descriptor">The diagnostic rule descriptor.</param>
    /// <param name="messageArgs">Arguments for the message format string.</param>
    public DiagnosticInfo(DiagnosticDescriptor descriptor, params string[] messageArgs)
    {
        Descriptor = descriptor;
        MessageArgs = messageArgs;
    }

    public bool Equals(DiagnosticInfo other)
    {
        if (Descriptor.Id != other.Descriptor.Id) return false;
        if (MessageArgs.Length != other.MessageArgs.Length) return false;
        for (var i = 0; i < MessageArgs.Length; i++)
            if (MessageArgs[i] != other.MessageArgs[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

    public override int GetHashCode()
    {
        var hash = Descriptor.Id.GetHashCode();
        foreach (var arg in MessageArgs)
            hash = hash * 31 + arg.GetHashCode();
        return hash;
    }
}
