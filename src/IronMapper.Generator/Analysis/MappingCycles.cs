using System;
using System.Collections.Generic;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// IM0004: the graph of the generated mappings of a compilation, for the candidates of automatically mapped nested
/// objects (<see cref="PropertyTypeCheck.CyclePropertyKey"/>). A node is a type pair; an edge is a property whose
/// mapping calls another generated mapping: a nested object or the elements of a collection, mapped automatically
/// (<c>MapFrom</c>, <c>Ignore</c> and converters add no edge, so they break a cycle). A mapping with <c>ConvertUsing</c>
/// has no edges: its <c>…Core</c> calls the converter, not the mappings of its properties.
/// </summary>
internal sealed class MappingCycles
{
    private readonly Dictionary<string, MappingDescriptor> _byPair = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Edge>> _edges = new(StringComparer.Ordinal);

    /// <param name="generated">The descriptors of the mappings the plan generates.</param>
    /// <param name="plan">The plan: which pair maps a nested object or an element.</param>
    public MappingCycles(IEnumerable<MappingDescriptor> generated, MappingPlan plan)
    {
        foreach (var descriptor in generated)
        {
            _byPair[descriptor.PairKey] = descriptor;
            var edges = new List<Edge>();
            _edges[descriptor.PairKey] = edges;
            if (descriptor.WholeObjectConverterType is not null || descriptor.WholeObjectLambdaBody is not null) continue;

            foreach (var prop in descriptor.PropertyMappings)
            {
                var nested = MapperCodeEmitter.IsMappedNestedObject(prop);
                if (!nested && !MapperCodeEmitter.UsesCollectionHelper(prop)) continue;
                if (prop.MappingPairs.Length == 0 || plan.FindFirstGenerated(prop.MappingPairs) is not { } target) continue;
                edges.Add(new Edge(prop.DestPropertyName, !nested, target.PairKey));
            }
        }
    }

    /// <summary>
    /// The cycle that the nested object <paramref name="property"/> of <paramref name="descriptor"/> closes, as the chain
    /// of mappings for the message (<c>App.Order → App.OrderDto, Customer: App.Customer → App.CustomerDto, Orders
    /// (elements): App.Order → App.OrderDto</c>), or <see langword="null"/> when it is on no cycle.
    /// </summary>
    public string? FindCycle(MappingDescriptor descriptor, string property)
    {
        var start = descriptor.PairKey;
        if (!_edges.TryGetValue(start, out var edges)) return null;
        var first = edges.FirstOrDefault(e => e.Property == property);
        if (first is null) return null;

        // Breadth first from the target back to the start: the shortest chain, deterministic (properties in order).
        var previous = new Dictionary<string, (string from, Edge edge)>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var found = first.Target == start;
        if (!found)
        {
            previous[first.Target] = (start, first);
            queue.Enqueue(first.Target);
        }

        while (!found && queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!_edges.TryGetValue(node, out var next)) continue;
            foreach (var edge in next)
            {
                if (edge.Target == start)
                {
                    previous[start] = (node, edge);
                    found = true;
                    break;
                }
                if (previous.ContainsKey(edge.Target)) continue;
                previous[edge.Target] = (node, edge);
                queue.Enqueue(edge.Target);
            }
        }

        if (!found) return null;

        var chain = new List<Edge>();
        if (first.Target == start)
        {
            chain.Add(first);
        }
        else
        {
            for (var node = start; ; )
            {
                var (from, edge) = previous[node];
                chain.Add(edge);
                if (from == start && edge == first) break;
                node = from;
            }
            chain.Reverse();
        }

        var parts = new List<string> { Display(start) };
        foreach (var edge in chain)
            parts.Add($"{edge.Property}{(edge.IsCollection ? " (elements)" : string.Empty)}: {Display(edge.Target)}");
        return string.Join(", ", parts);
    }

    private string Display(string pairKey)
    {
        var descriptor = _byPair[pairKey];
        return $"{Strip(descriptor.SourceFullName)} → {Strip(descriptor.DestFullName)}";
    }

    private static string Strip(string? fullName)
        => fullName is null ? string.Empty
            : fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;

    private sealed class Edge
    {
        public Edge(string property, bool isCollection, string target)
        {
            Property = property;
            IsCollection = isCollection;
            Target = target;
        }

        public string Property { get; }

        public bool IsCollection { get; }

        public string Target { get; }
    }
}
