using System.Globalization;
using System.Text.RegularExpressions;

namespace Comparison.Cli.Parsing;

/// <summary>A recognised line of run-all.ps1's stdout. The patterns never depend on the dash of the header lines.</summary>
internal abstract record RunAllEvent;

/// <summary><c>IronMapper comparison benchmarks — Dry run</c>.</summary>
internal sealed record RunAllMode(string Mode) : RunAllEvent;

/// <summary><c>IronMapper: ...; results: &lt;dir&gt;</c> — the results folder relative to benchmarks/.</summary>
internal sealed record RunAllResultsDir(string Dir) : RunAllEvent;

/// <summary><c>Expected duration on the reference machine: ~N min (Build 1, Tests 1, ...)</c> — the steps of this run.</summary>
internal sealed record RunAllPlan(int TotalMinutes, IReadOnlyList<(string Step, int Minutes)> Steps) : RunAllEvent;

internal sealed record RunAllPreflight(PreflightCheck Check) : RunAllEvent;

/// <summary><c>==> Step (~N min) — HH:mm</c>.</summary>
internal sealed record RunAllStepStarted(string Step, int EstimateMinutes) : RunAllEvent;

/// <summary><c>    log: &lt;path&gt;</c> — the log of a dotnet command of the current step (Invoke-Dotnet).</summary>
internal sealed record RunAllLog(string Path) : RunAllEvent;

/// <summary><c>    done in h:mm:ss</c>.</summary>
internal sealed record RunAllStepDone(TimeSpan Elapsed) : RunAllEvent;

/// <summary><c>All done in h:mm:ss. Results: &lt;RESULTS.md&gt;</c>.</summary>
internal sealed record RunAllDone(TimeSpan Elapsed, string? ResultsFile) : RunAllEvent;

internal static partial class RunAllOutput
{
    public static RunAllEvent? Parse(string line)
    {
        Match m;
        if ((m = StepStarted().Match(line)).Success)
            return new RunAllStepStarted(m.Groups["step"].Value, int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture));
        if ((m = StepDone().Match(line)).Success)
            return new RunAllStepDone(ParseDuration(m.Groups[1].Value));
        if ((m = Log().Match(line)).Success)
            return new RunAllLog(m.Groups[1].Value.Trim());
        if ((m = Done().Match(line)).Success)
            return new RunAllDone(ParseDuration(m.Groups["elapsed"].Value), m.Groups["file"].Success ? m.Groups["file"].Value.Trim() : null);
        if ((m = ResultsDir().Match(line)).Success)
            return new RunAllResultsDir(m.Groups[1].Value.Trim());
        if ((m = Mode().Match(line)).Success)
            return new RunAllMode(m.Groups[1].Value);
        if ((m = Plan().Match(line)).Success)
        {
            var steps = PlanItem().Matches(m.Groups["steps"].Value)
                .Select(item => (item.Groups[1].Value, int.Parse(item.Groups[2].Value, CultureInfo.InvariantCulture)))
                .ToList();
            return new RunAllPlan(int.Parse(m.Groups["total"].Value, CultureInfo.InvariantCulture), steps);
        }
        return PreflightCheck.TryParse(line) is { } check ? new RunAllPreflight(check) : null;
    }

    private static TimeSpan ParseDuration(string text) =>
        TimeSpan.TryParseExact(text, @"h\:mm\:ss", CultureInfo.InvariantCulture, out TimeSpan value) ? value : TimeSpan.Zero;

    [GeneratedRegex(@"^==> (?<step>\w+) \(~(?<min>\d+) min\)")]
    private static partial Regex StepStarted();

    [GeneratedRegex(@"^\s+done in (\d+:\d{2}:\d{2})\s*$")]
    private static partial Regex StepDone();

    [GeneratedRegex(@"^\s+log: (.+)$")]
    private static partial Regex Log();

    [GeneratedRegex(@"^All done in (?<elapsed>\d+:\d{2}:\d{2})\.(?: Results: (?<file>.+))?")]
    private static partial Regex Done();

    [GeneratedRegex(@"^IronMapper: .*; results: (.+)$")]
    private static partial Regex ResultsDir();

    [GeneratedRegex(@"^IronMapper comparison benchmarks\b.*?\b(Full|Quick|Dry) run\s*$")]
    private static partial Regex Mode();

    [GeneratedRegex(@"^Expected duration on the reference machine: ~(?<total>\d+) min \((?<steps>.*)\)\s*$")]
    private static partial Regex Plan();

    [GeneratedRegex(@"(\w+) (\d+)")]
    private static partial Regex PlanItem();
}
