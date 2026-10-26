using Comparison.Cli.Artifacts;
using Comparison.Cli.Processes;

namespace Comparison.Cli.Commands;

/// <summary>
/// The parameters of a quick A/B run. A null value is not passed: the script's default applies. Lists go to the
/// script as one comma-separated argument (run-quick.ps1 splits them, which also works with pwsh -File).
/// </summary>
internal sealed record QuickOptions
{
    /// <summary>Short benchmark names (Nested), or null for the script's default filter.</summary>
    public IReadOnlyList<string>? Benchmarks { get; init; }

    public IReadOnlyList<string>? Frameworks { get; init; }

    public string? PublishedVersion { get; init; }

    public string? Branch { get; init; }

    public bool Dry { get; init; }

    public bool CheckOnly { get; init; }

    public IReadOnlyList<string> ToScriptArguments()
    {
        var args = new List<string>();
        if (Benchmarks is { Count: > 0 })
        {
            args.AddRange(["-Filter", string.Join(",", Benchmarks.Select(BenchmarkCatalog.ToFilter))]);
        }

        if (Frameworks is { Count: > 0 })
        {
            args.AddRange(["-Frameworks", string.Join(",", Frameworks)]);
        }

        if (PublishedVersion is not null)
        {
            args.AddRange(["-PublishedVersion", PublishedVersion]);
        }

        if (Branch is not null)
        {
            args.AddRange(["-Branch", Branch]);
        }

        if (Dry)
        {
            args.Add("-Dry");
        }

        if (CheckOnly)
        {
            args.Add("-CheckOnly");
        }

        return args;
    }

    /// <summary>The same run as a CLI command to copy, e.g. <c>./bench.ps1 quick --filter Nested --dry</c>.</summary>
    public string ToBenchCommand()
    {
        var args = new List<string> { "quick" };
        if (Benchmarks is { Count: > 0 })
        {
            args.AddRange(["--filter", string.Join(",", Benchmarks)]);
        }

        if (Frameworks is { Count: > 0 })
        {
            args.AddRange(["--frameworks", string.Join(",", Frameworks)]);
        }

        if (PublishedVersion is not null)
        {
            args.AddRange(["--published-version", PublishedVersion]);
        }

        if (Branch is not null)
        {
            args.AddRange(["--branch", Branch]);
        }

        if (Dry)
        {
            args.Add("--dry");
        }

        if (CheckOnly)
        {
            args.Add("--check-only");
        }

        return BenchCommand.Display(args);
    }
}

internal static class BenchCommand
{
    public static string Wrapper => OperatingSystem.IsWindows() ? "./bench.ps1" : "./bench.sh";

    public static string Display(IEnumerable<string> args) =>
        string.Join(' ', args.Select(ScriptCommand.Quote).Prepend(Wrapper));
}
