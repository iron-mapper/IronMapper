using System;
using System.Collections.Generic;
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

    /// <summary>Where the diagnostic points in the user's code, or <see langword="null"/> for no location.</summary>
    public LocationInfo? Location { get; }

    /// <summary>Further related locations (e.g. the other definitions of a duplicated mapping).</summary>
    public EquatableArray<LocationInfo> AdditionalLocations { get; }

    /// <summary>
    /// <see cref="Diagnostic.Properties"/> as <c>key=value</c> entries, read by the code fixes (e.g. the destination
    /// property names for IM0006).
    /// </summary>
    public EquatableArray<string> Properties { get; }

    /// <summary>Initialises a new <see cref="DiagnosticInfo"/> without a location.</summary>
    /// <param name="descriptor">The diagnostic rule descriptor.</param>
    /// <param name="messageArgs">Arguments for the message format string.</param>
    public DiagnosticInfo(DiagnosticDescriptor descriptor, params string[] messageArgs)
        : this(descriptor, ImmutableArray.Create(messageArgs), null, default, default)
    {
    }

    private DiagnosticInfo(DiagnosticDescriptor descriptor, EquatableArray<string> messageArgs, LocationInfo? location,
        EquatableArray<LocationInfo> additionalLocations, EquatableArray<string> properties)
    {
        Descriptor = descriptor;
        MessageArgs = messageArgs;
        Location = location;
        AdditionalLocations = additionalLocations;
        Properties = properties;
    }

    /// <summary>A copy that points to <paramref name="location"/> (no location when <see langword="null"/>).</summary>
    public DiagnosticInfo WithLocation(LocationInfo? location)
        => new(Descriptor, MessageArgs, location, AdditionalLocations, Properties);

    /// <summary>A copy with the related <paramref name="locations"/>.</summary>
    public DiagnosticInfo WithAdditionalLocations(IEnumerable<LocationInfo> locations)
        => new(Descriptor, MessageArgs, Location, ImmutableArray.CreateRange(locations), Properties);

    /// <summary>A copy with <paramref name="value"/> appended to <see cref="MessageArgs"/>.</summary>
    public DiagnosticInfo WithMessageArg(string value)
        => new(Descriptor, MessageArgs.AsImmutableArray().Add(value), Location, AdditionalLocations, Properties);

    /// <summary>The value of the property <paramref name="key"/>, or <see langword="null"/>.</summary>
    public string? GetProperty(string key)
    {
        string prefix = key + "=";
        foreach (string entry in Properties)
        {
            if (entry.StartsWith(prefix, StringComparison.Ordinal))
            {
                return entry.Substring(prefix.Length);
            }
        }

        return null;
    }

    /// <summary>A copy with the property <paramref name="key"/> set to <paramref name="value"/>.</summary>
    public DiagnosticInfo WithProperty(string key, string value)
        => new(Descriptor, MessageArgs, Location, AdditionalLocations, Properties.AsImmutableArray().Add(key + "=" + value));

    /// <summary>
    /// Creates the diagnostic to report. <paramref name="resolve"/> turns a <see cref="LocationInfo"/> into the location
    /// in the current compilation (see <see cref="LocationInfo.ToLocation(System.Collections.Generic.IReadOnlyDictionary{string, SyntaxTree?})"/>).
    /// </summary>
    public Diagnostic ToDiagnostic(Func<LocationInfo, Location>? resolve = null)
    {
        resolve ??= static location => location.ToLocation();

        object[] args = new object[MessageArgs.Length];
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = MessageArgs[i];
        }

        var additional = new List<Location>(AdditionalLocations.Length);
        foreach (var location in AdditionalLocations)
        {
            additional.Add(resolve(location));
        }

        ImmutableDictionary<string, string?>? properties = null;
        if (Properties.Length > 0)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string?>();
            foreach (string entry in Properties)
            {
                int separator = entry.IndexOf('=');
                builder[entry.Substring(0, separator)] = entry.Substring(separator + 1);
            }
            properties = builder.ToImmutable();
        }

        return Diagnostic.Create(Descriptor, Location is { } at ? resolve(at) : Microsoft.CodeAnalysis.Location.None, additional,
            properties, args);
    }

    /// <inheritdoc/>
    public bool Equals(DiagnosticInfo other)
        => Equals(Descriptor, other.Descriptor)
           && MessageArgs.Equals(other.MessageArgs)
           && Nullable.Equals(Location, other.Location)
           && AdditionalLocations.Equals(other.AdditionalLocations)
           && Properties.Equals(other.Properties);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = Descriptor?.Id.GetHashCode() ?? 0;
        hash = hash * 31 + MessageArgs.GetHashCode();
        hash = hash * 31 + Location.GetHashCode();
        hash = hash * 31 + AdditionalLocations.GetHashCode();
        return hash * 31 + Properties.GetHashCode();
    }
}
