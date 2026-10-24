using System.Text.RegularExpressions;

namespace Comparison.Cli.Artifacts;

/// <summary>
/// The runtime benchmark classes: src/Comparison.Benchmarks/*Benchmark.cs without StartupBenchmark (cold start, run by
/// run-startup.ps1) — the same set as $runtimeClasses of run-all.ps1. A short name (Nested) maps to the BenchmarkDotNet
/// filter the scripts use (*.NestedBenchmark.*).
/// </summary>
internal static partial class BenchmarkCatalog
{
    private static readonly string[] NotRuntime = ["Startup"];

    public static IReadOnlyList<string> Discover(string benchmarkSourcesDir) =>
        Directory.Exists(benchmarkSourcesDir)
            ? Directory.GetFiles(benchmarkSourcesDir, "*Benchmark.cs")
                .Select(file => Path.GetFileNameWithoutExtension(file)[..^"Benchmark".Length])
                .Where(name => name.Length > 0 && !NotRuntime.Contains(name, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    public static string ToFilter(string name) => $"*.{name}Benchmark.*";

    /// <summary>The short name of a filter of the form *.XBenchmark.*, or the filter itself.</summary>
    public static string ToName(string filter)
    {
        Match m = FilterPattern().Match(filter);
        return m.Success ? m.Groups[1].Value : filter;
    }

    /// <summary>
    /// Filters for the given names (case-insensitive). Unknown names are returned in <paramref name="unknown"/>.
    /// </summary>
    public static IReadOnlyList<string> ToFilters(IEnumerable<string> names, IReadOnlyList<string> known, out IReadOnlyList<string> unknown)
    {
        var filters = new List<string>();
        var missing = new List<string>();
        foreach (string raw in names)
        {
            string name = raw.Trim();
            if (name.EndsWith("Benchmark", StringComparison.OrdinalIgnoreCase))
                name = name[..^"Benchmark".Length];
            string? match = known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                missing.Add(raw.Trim());
            else if (!filters.Contains(ToFilter(match)))
                filters.Add(ToFilter(match));
        }
        unknown = missing;
        return filters;
    }

    [GeneratedRegex(@"^\*\.(\w+)Benchmark\.\*$")]
    private static partial Regex FilterPattern();
}
