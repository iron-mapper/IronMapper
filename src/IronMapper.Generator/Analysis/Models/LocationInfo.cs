using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// A source location as pipeline data: file path, <see cref="TextSpan"/> and <see cref="LinePositionSpan"/>, compared by
/// value. A <see cref="Location"/> holds its syntax tree (and through it the compilation) and is compared by reference,
/// so it must not be kept in the pipeline; <see cref="ToLocation(IReadOnlyDictionary{string, SyntaxTree?})"/> recreates it when the diagnostic is reported.
/// </summary>
internal readonly struct LocationInfo : IEquatable<LocationInfo>
{
    public LocationInfo(string filePath, TextSpan span, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
    }

    /// <summary>Path of the file (<see cref="SyntaxTree.FilePath"/>).</summary>
    public string FilePath { get; }

    /// <summary>Position in the file.</summary>
    public TextSpan Span { get; }

    /// <summary>Lines and columns of <see cref="Span"/>.</summary>
    public LinePositionSpan LineSpan { get; }

    /// <summary>The location of <paramref name="node"/>, or <see langword="null"/> for <see langword="null"/>.</summary>
    public static LocationInfo? From(SyntaxNode? node) => node is null ? null : From(node.GetLocation());

    /// <summary>
    /// The location of <paramref name="location"/> when it is in source, otherwise <see langword="null"/> (a symbol from a
    /// referenced assembly has no source location).
    /// </summary>
    public static LocationInfo? From(Location? location)
    {
        if (location is not { IsInSource: true, SourceTree: { } tree }) return null;
        return new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    /// <summary>The first source location of <paramref name="symbol"/>, or <see langword="null"/>.</summary>
    public static LocationInfo? From(ISymbol? symbol)
    {
        if (symbol is null) return null;
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource) return From(location);
        }

        return null;
    }

    /// <summary>
    /// The location to report without the compilation: a location in a file (no syntax tree), so <c>.editorconfig</c>
    /// severities and code fixes do not apply to it. Prefer <see cref="ToLocation(IReadOnlyDictionary{string, SyntaxTree?})"/>.
    /// </summary>
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    /// <summary>
    /// The location in the syntax tree of the current compilation with this <see cref="FilePath"/>, so the IDE underlines
    /// it, <c>.editorconfig</c> severities apply (the compiler reads them from the tree) and code fixes find their
    /// document. Falls back to <see cref="ToLocation()"/> when no single tree has the path (e.g. trees without paths).
    /// </summary>
    /// <param name="trees">Trees of the compilation by path; <see langword="null"/> for a path used by several trees.</param>
    public Location ToLocation(IReadOnlyDictionary<string, SyntaxTree?> trees)
        => trees.TryGetValue(FilePath, out var tree) && tree is not null && Span.End <= tree.Length
            ? Location.Create(tree, Span)
            : ToLocation();

    /// <summary>Trees of <paramref name="compilation"/> by <see cref="SyntaxTree.FilePath"/> (<see langword="null"/> when a path is not unique).</summary>
    public static IReadOnlyDictionary<string, SyntaxTree?> TreesByPath(Compilation compilation)
    {
        var trees = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
            trees[tree.FilePath] = trees.ContainsKey(tree.FilePath) ? null : tree;
        return trees;
    }

    /// <inheritdoc/>
    public bool Equals(LocationInfo other)
        => FilePath == other.FilePath && Span.Equals(other.Span) && LineSpan.Equals(other.LineSpan);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LocationInfo other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => ((FilePath?.GetHashCode() ?? 0) * 31 + Span.GetHashCode()) * 31 + LineSpan.GetHashCode();
}
