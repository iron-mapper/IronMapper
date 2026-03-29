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
    public DiagnosticDescriptor Descriptor { get; }
    public string[] MessageArgs { get; }

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
