using System.ComponentModel;
using Comparison.Cli.Artifacts;
using Comparison.Cli.Monitoring;
using Comparison.Cli.Parsing;
using Comparison.Cli.Processes;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Comparison.Cli.Commands;

internal sealed class QuickSettings : CommandSettings
{
    [CommandOption("--filter <NAMES>")]
    [Description("Benchmark classes, comma-separated short names (Flat,Nested,...). Default: the script's (-Filter).")]
    public string? Filter { get; init; }

    [CommandOption("--frameworks <TFMS>")]
    [Description("Target frameworks, comma-separated (net10.0,net8.0). Default: the script's (-Frameworks).")]
    public string? Frameworks { get; init; }

    [CommandOption("--published-version <VERSION>")]
    [Description("The published IronMapper version to compare with. Default: the script's (-PublishedVersion).")]
    public string? PublishedVersion { get; init; }

    [CommandOption("--branch <BRANCH>")]
    [Description("Fail the pre-flight unless this branch is checked out (-Branch).")]
    public string? Branch { get; init; }

    [CommandOption("--dry")]
    [Description("One iteration (--job Dry): checks the commands in a few minutes; the numbers mean nothing (-Dry).")]
    public bool Dry { get; init; }

    [CommandOption("--check-only")]
    [Description("Only the pre-flight checks (-CheckOnly).")]
    public bool CheckOnly { get; init; }

    public override ValidationResult Validate()
    {
        BenchPaths paths = BenchPaths.Current;
        if (Filter is not null)
        {
            IReadOnlyList<string> known = BenchmarkCatalog.Discover(paths.BenchmarkSourcesDir);
            BenchmarkCatalog.ToFilters(Split(Filter), known, out IReadOnlyList<string> unknown);
            if (unknown.Count > 0 || Split(Filter).Count == 0)
                return ValidationResult.Error($"Unknown benchmark '{string.Join(", ", unknown)}'. Known: {string.Join(", ", known)}.");
        }
        if (Frameworks is not null)
        {
            IReadOnlyList<string> known = BenchEnvironment.SuiteFrameworks(paths.BenchmarksDir);
            var unknown = Split(Frameworks).Where(f => !known.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0 || Split(Frameworks).Count == 0)
                return ValidationResult.Error($"Unknown framework '{string.Join(", ", unknown)}'. The suite targets: {string.Join(", ", known)}.");
        }
        return ValidationResult.Success();
    }

    public QuickOptions ToOptions()
    {
        IReadOnlyList<string> known = BenchmarkCatalog.Discover(BenchPaths.Current.BenchmarkSourcesDir);
        return new QuickOptions
        {
            Benchmarks = Filter is null ? null : BenchmarkCatalog.ToFilters(Split(Filter), known, out _).Select(BenchmarkCatalog.ToName).ToList(),
            Frameworks = Frameworks is null ? null : Split(Frameworks).Select(f => f.ToLowerInvariant()).Distinct().ToList(),
            PublishedVersion = string.IsNullOrWhiteSpace(PublishedVersion) ? null : PublishedVersion.Trim(),
            Branch = string.IsNullOrWhiteSpace(Branch) ? null : Branch.Trim(),
            Dry = Dry,
            CheckOnly = CheckOnly,
        };
    }

    internal static IReadOnlyList<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary><c>quick ...</c>: run-quick.ps1 with the given flags, at once (no confirmation; --check-only for the checks).</summary>
internal sealed class QuickCommand : AsyncCommand<QuickSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, QuickSettings settings, CancellationToken cancellationToken) =>
        QuickRunner.RunAsync(BenchPaths.Current, settings.ToOptions(), printBenchCommand: false);
}

/// <summary>Runs run-quick.ps1 (checks only, or a run with progress) and shows the outcome.</summary>
internal static class QuickRunner
{
    public static async Task<int> RunAsync(BenchPaths paths, QuickOptions options, bool printBenchCommand)
    {
        if (Tools.CheckPrerequisites() is { } missing)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return 1;
        }
        var command = new ScriptCommand(paths.RunQuickScript, options.ToScriptArguments(), paths.BenchmarksDir);
        ScriptRunner.PrintCommand(command, paths);

        int exitCode = options.CheckOnly
            ? (await CheckAsync(command).ConfigureAwait(false)).ExitCode
            : await RunWithProgressAsync(command).ConfigureAwait(false);

        if (printBenchCommand)
        {
            AnsiConsole.MarkupLine("[grey]The same with the CLI, without the menu:[/]");
            Console.WriteLine("  " + options.ToBenchCommand());
        }
        return exitCode;
    }

    /// <summary>run-quick.ps1 -CheckOnly: the checks as a table, and the script's error when one failed.</summary>
    public static async Task<(int ExitCode, IReadOnlyList<PreflightCheck> Checks, string? RunsPlanned)> CheckAsync(ScriptCommand command)
    {
        var lines = new List<string>();
        using var interrupts = new InterruptHandler(confirm: false);
        Task<ScriptResult> run = ScriptRunner.RunAsync(command, line => { lock (lines) lines.Add(line); }, _ => { }, interrupts);
        ScriptResult result = Terminal.Plain
            ? await run.ConfigureAwait(false)
            : await AnsiConsole.Status().StartAsync("Pre-flight checks (restore, nuget.org comparison)...", _ => run).ConfigureAwait(false);

        var events = lines.Select(QuickOutput.Parse).ToList();
        var checks = events.OfType<QuickPreflight>().Select(e => e.Check).ToList();
        string? runs = events.OfType<QuickRunsPlanned>().FirstOrDefault()?.Text;
        CheckView.Write(checks);
        if (result.Interrupted)
            AnsiConsole.MarkupLine("[yellow]Stopped.[/]");
        else if (result.ExitCode != 0)
            AnsiConsole.MarkupLine($"[red]Pre-flight failed (exit code {result.ExitCode}):[/] {Markup.Escape(result.ErrorMessage)}");
        else
            AnsiConsole.MarkupLine("[green]Checks passed.[/]");
        return (result.ExitCode, checks, runs);
    }

    private static async Task<int> RunWithProgressAsync(ScriptCommand command)
    {
        var monitor = new QuickMonitor();
        ScriptResult result = await RunSession.RunAsync(command, monitor, keepAwake: true).ConfigureAwait(false);
        if (result.Interrupted)
            return result.ExitCode;

        bool started = monitor.Root is { } root && Directory.Exists(root);
        if (!started)
        {
            // Stopped before any run: the checks (or the arguments) failed.
            if (!Terminal.Plain)
                CheckView.Write(monitor.Checks);
            AnsiConsole.MarkupLine($"[red]run-quick.ps1 failed before measuring (exit code {result.ExitCode}):[/] {Markup.Escape(result.ErrorMessage)}");
            return result.ExitCode;
        }

        QuickRun run = QuickRun.Load(monitor.Root!);
        if (Terminal.Plain)
        {
            // The script has printed summary.md already.
            Console.WriteLine();
            Console.WriteLine("Runs:");
            foreach (QuickVariantRun r in run.Runs)
                Console.WriteLine($"  {r.Variant} {r.Framework}: {r.Status.Text} (log: {Path.Combine(r.Dir, "run.log")})");
            Console.WriteLine($"Folder: {run.Dir}");
        }
        else
        {
            AnsiConsole.WriteLine();
            ResultsView.WriteQuickRun(run);
            AnsiConsole.MarkupLine($"[grey]Logs: build.log and run.log in {Markup.Escape(string.Join(", ", run.Runs.Select(r => Path.GetFileName(r.Dir))))}[/]");
        }
        if (result.ExitCode != 0 && result.Stderr.Count > 0)
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.ErrorMessage)}[/]");
        AnsiConsole.MarkupLine(result.ExitCode == 0
            ? "[green]Done.[/]"
            : $"[red]run-quick.ps1 exited with {result.ExitCode}[/] (a run is not ok, or the script stopped).");
        return result.ExitCode;
    }
}
