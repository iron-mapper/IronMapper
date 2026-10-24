using System.Globalization;
using System.Text.RegularExpressions;

namespace Comparison.Cli.Parsing;

/// <summary>
/// Progress of one BenchmarkDotNet process, from its log (BenchmarkDotNet 0.15.8):
/// <list type="bullet">
///   <item><c>// ***** Found N benchmark(s) in total *****</c> — all cases of the run (the per-class
///   <c>// Found N benchmarks:</c> is not the total);</item>
///   <item><c>// Benchmark: Class.Method: Job [Params]</c> — the current case;</item>
///   <item><c>// Launch: i / n</c>;</item>
///   <item><c>// ** Remained N (x %) benchmark(s) to run. Estimated finish ... (Xh Ym from now) **</c> — counted over all
///   classes; the percentage and the date depend on the culture and are not read;</item>
///   <item><c>Global total time: ...</c> — the end.</item>
/// </list>
/// Unknown lines are ignored. <see cref="Recognized"/> stays false until a known line appears.
/// </summary>
internal sealed partial class BdnProgress
{
    public int? Total { get; private set; }

    public int? Remaining { get; private set; }

    public string? CurrentBenchmark { get; private set; }

    public int? Launch { get; private set; }

    public int? LaunchCount { get; private set; }

    /// <summary>Time left as BenchmarkDotNet estimates it, known after the first finished case.</summary>
    public TimeSpan? Eta { get; private set; }

    public bool Finished { get; private set; }

    public bool Recognized { get; private set; }

    /// <summary>Cases finished: total minus remaining (0 before the first "Remained").</summary>
    public int Completed => Total is { } total ? total - (Remaining ?? total) : 0;

    /// <summary>Reads one line; true when it changed the progress.</summary>
    public bool Feed(string line)
    {
        if (!line.StartsWith("//", StringComparison.Ordinal))
        {
            if (line.StartsWith("Global total time:", StringComparison.Ordinal))
                return Set(() => Finished = true);
            return false;
        }

        Match m;
        if ((m = FoundTotal().Match(line)).Success)
            return Set(() => Total = Int(m.Groups[1]));
        if ((m = Benchmark().Match(line)).Success)
        {
            string name = m.Groups["name"].Value.Trim();
            if (m.Groups["params"].Success)
                name += $" [{m.Groups["params"].Value}]";
            return Set(() =>
            {
                CurrentBenchmark = name;
                Launch = null;
                LaunchCount = null;
            });
        }
        if ((m = LaunchLine().Match(line)).Success)
        {
            return Set(() =>
            {
                Launch = Int(m.Groups[1]);
                LaunchCount = Int(m.Groups[2]);
            });
        }
        if ((m = Remained().Match(line)).Success)
        {
            return Set(() =>
            {
                Remaining = Int(m.Groups["left"]);
                if (m.Groups["h"].Success)
                    Eta = new TimeSpan(Int(m.Groups["h"]), Int(m.Groups["m"]), 0);
            });
        }
        return false;
    }

    /// <summary>One line, e.g. <c>7/24 · NestedBenchmark.IronMapper · launch 2/3 · ETA 0h 12m</c>.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Total is { } total)
            parts.Add($"{Completed}/{total}");
        if (CurrentBenchmark is not null && !Finished)
            parts.Add(CurrentBenchmark);
        if (Launch is { } launch && LaunchCount is { } count && count > 1 && !Finished)
            parts.Add($"launch {launch}/{count}");
        if (Eta is { } eta && !Finished)
            parts.Add($"ETA {(int)eta.TotalHours}h {eta.Minutes}m");
        if (Finished)
            parts.Add("finished");
        return string.Join(" · ", parts);
    }

    private bool Set(Action change)
    {
        change();
        Recognized = true;
        return true;
    }

    private static int Int(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^// \*+ Found (\d+) benchmark\(s\) in total \*+")]
    private static partial Regex FoundTotal();

    // The method name has no colon; parameters, when any, close the line in brackets.
    [GeneratedRegex(@"^// Benchmark: (?<name>[^:]+): (?<job>.*?)(?: \[(?<params>[^\]]*)\])?\s*$")]
    private static partial Regex Benchmark();

    [GeneratedRegex(@"^// Launch: (\d+) / (\d+)")]
    private static partial Regex LaunchLine();

    [GeneratedRegex(@"^// \*\* Remained (?<left>\d+) \(.*?\) benchmark\(s\) to run\.(?:.*\((?<h>\d+)h (?<m>\d+)m from now\))?")]
    private static partial Regex Remained();
}
