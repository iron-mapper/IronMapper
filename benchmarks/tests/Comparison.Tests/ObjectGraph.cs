using System.Collections;
using System.Reflection;

namespace Comparison.Tests;

/// <summary>Collects every reference-type object reachable from a root (public properties and enumerable items).</summary>
internal static class ObjectGraph
{
    public static HashSet<object> Collect(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root, seen);
        return seen;
    }

    private static void Visit(object? value, HashSet<object> seen)
    {
        // Strings are immutable and shared by design; value types have no identity.
        if (value is null or string || value.GetType().IsValueType || !seen.Add(value))
            return;

        if (value is IEnumerable items)
        {
            foreach (var item in items)
                Visit(item, seen);
            return;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
                Visit(property.GetValue(value), seen);
        }
    }
}
