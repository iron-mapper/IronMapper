using System.Text.RegularExpressions;

namespace Comparison.Cli.Parsing;

/// <summary>A recognised line of run-quick.ps1's stdout.</summary>
internal abstract record QuickEvent;

/// <summary>
/// <c>Filter: a b; frameworks: net10.0, net8.0; job: ...</c> — what the script will run, after its own parsing of the
/// parameters; the frameworks in the order of the runs.
/// </summary>
internal sealed record QuickHeader(IReadOnlyList<string> Filter, IReadOnlyList<string> Frameworks, string Job) : QuickEvent;

/// <summary><c>Runs: N framework(s) x 2 variants; ...</c> — the last line of the checks.</summary>
internal sealed record QuickRunsPlanned(string Text) : QuickEvent;

/// <summary><c>Output: &lt;root&gt;</c> — the run folder (printed before the checks; it exists only once runs start).</summary>
internal sealed record QuickOutputRoot(string Path) : QuickEvent;

internal sealed record QuickPreflight(PreflightCheck Check) : QuickEvent;

/// <summary><c>Checks passed (-CheckOnly: nothing measured).</c></summary>
internal sealed record QuickChecksPassed : QuickEvent;

internal enum QuickStageKind
{
    Build,
    Run,
    Result,
}

/// <summary>
/// <c>[HH:mm:ss] &lt;variant&gt; on &lt;framework&gt;: build|run|&lt;status&gt;</c>; the status is Get-RunStatus's text
/// (ok, FAILED (...), INVALID: ..., not run).
/// </summary>
internal sealed record QuickStage(TimeOnly Time, string Variant, string Framework, QuickStageKind Kind, string Text) : QuickEvent;

/// <summary><c>Summary: &lt;path&gt;</c> — the last line of a run (also of -SummarizeOnly).</summary>
internal sealed record QuickSummaryWritten(string Path) : QuickEvent;

/// <summary>
/// <c># Quick A/B: ...</c> — the script prints the whole summary.md before the Summary line; everything from here to
/// <see cref="QuickSummaryWritten"/> is that copy.
/// </summary>
internal sealed record QuickSummaryEcho : QuickEvent;

internal static partial class QuickOutput
{
    public static QuickEvent? Parse(string line)
    {
        Match m;
        if ((m = Stage().Match(line)).Success)
        {
            string text = m.Groups["text"].Value.Trim();
            QuickStageKind kind = text switch
            {
                "build" => QuickStageKind.Build,
                "run" => QuickStageKind.Run,
                _ => QuickStageKind.Result,
            };
            return new QuickStage(TimeOnly.Parse(m.Groups["time"].Value), m.Groups["variant"].Value, m.Groups["framework"].Value, kind, text);
        }
        if ((m = OutputRoot().Match(line)).Success)
            return new QuickOutputRoot(m.Groups[1].Value.Trim());
        if ((m = Header().Match(line)).Success)
        {
            return new QuickHeader(
                m.Groups["filter"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                m.Groups["frameworks"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                m.Groups["job"].Value.Trim());
        }
        if (line.StartsWith("Runs: ", StringComparison.Ordinal))
            return new QuickRunsPlanned(line["Runs: ".Length..].Trim());
        if ((m = SummaryPath().Match(line)).Success)
            return new QuickSummaryWritten(m.Groups[1].Value.Trim());
        if (line.StartsWith("# Quick A/B", StringComparison.Ordinal))
            return new QuickSummaryEcho();
        if (line.StartsWith("Checks passed", StringComparison.Ordinal))
            return new QuickChecksPassed();
        return PreflightCheck.TryParse(line) is { } check ? new QuickPreflight(check) : null;
    }

    [GeneratedRegex(@"^\[(?<time>\d{1,2}:\d{2}:\d{2})\] (?<variant>\S+) on (?<framework>\S+): (?<text>.+)$")]
    private static partial Regex Stage();

    [GeneratedRegex(@"^Filter: (?<filter>.*); frameworks: (?<frameworks>.*); job: (?<job>.*)$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^Output: (.+)$")]
    private static partial Regex OutputRoot();

    [GeneratedRegex(@"^Summary: (.+)$")]
    private static partial Regex SummaryPath();
}
