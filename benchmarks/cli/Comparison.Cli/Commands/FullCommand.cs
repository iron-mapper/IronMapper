using System.ComponentModel;
using Comparison.Cli.Artifacts;
using Comparison.Cli.Monitoring;
using Comparison.Cli.Parsing;
using Comparison.Cli.Processes;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Comparison.Cli.Commands;

internal enum FullMode
{
    Full,
    Quick,
    Dry,
}

/// <summary>The parameters of run-all.ps1. Unset values are not passed; the script's defaults apply.</summary>
internal sealed record FullOptions
{
    public FullMode Mode { get; init; }

    public bool Local { get; init; }

    public string? ResultsDir { get; init; }

    public bool SkipNet8 { get; init; }

    public bool LowMemory { get; init; }

    public bool Force { get; init; }

    public bool CheckOnly { get; init; }

    public IReadOnlyList<string> ToScriptArguments()
    {
        var args = new List<string>();
        if (Mode == FullMode.Quick)
            args.Add("-Quick");
        if (Mode == FullMode.Dry)
            args.Add("-Dry");
        if (Local)
            args.Add("-UseLocalIronMapper");
        if (ResultsDir is not null)
            args.AddRange(["-ResultsDir", ResultsDir]);
        if (SkipNet8)
            args.Add("-SkipNet8");
        if (LowMemory)
            args.Add("-LowMemory");
        if (Force)
            args.Add("-Force");
        if (CheckOnly)
            args.Add("-CheckOnly");
        return args;
    }

    public string ToBenchCommand()
    {
        var args = new List<string> { "full" };
        if (Mode != FullMode.Full)
            args.AddRange(["--mode", Mode.ToString().ToLowerInvariant()]);
        if (Local)
            args.Add("--local");
        if (ResultsDir is not null)
            args.AddRange(["--results-dir", ResultsDir]);
        if (SkipNet8)
            args.Add("--skip-net8");
        if (LowMemory)
            args.Add("--low-memory");
        if (Force)
            args.Add("--force");
        if (CheckOnly)
            args.Add("--check-only");
        return BenchCommand.Display(args);
    }

    /// <summary>A Quick or Dry run into results/ (the default) replaces the committed reports with numbers not for publication.</summary>
    public bool OverwritesPublishedResults => Mode != FullMode.Full && (ResultsDir is null || FullWizard.IsPublishedResults(ResultsDir)) && !CheckOnly;
}

internal sealed class FullSettings : CommandSettings
{
    [CommandOption("--mode <MODE>")]
    [Description("full (default: the published methodology), quick (-Quick: --job short, fewer runs) or dry (-Dry: one iteration, checks the commands).")]
    public string? Mode { get; init; }

    [CommandOption("--local")]
    [Description("Measure the local sources of ../src instead of the published package (-UseLocalIronMapper).")]
    public bool Local { get; init; }

    [CommandOption("--results-dir <DIR>")]
    [Description("Folder for this run's reports and raw data, relative to benchmarks/ (-ResultsDir). Default: results/.")]
    public string? ResultsDir { get; init; }

    [CommandOption("--skip-net8")]
    [Description("Do not run the runtime benchmarks on net8.0 (-SkipNet8).")]
    public bool SkipNet8 { get; init; }

    [CommandOption("--low-memory")]
    [Description("NativeAOT with a single ILC thread: less memory, slower (-LowMemory).")]
    public bool LowMemory { get; init; }

    [CommandOption("--force")]
    [Description("Continue although less memory is free than the NativeAOT publish needs (-Force).")]
    public bool Force { get; init; }

    [CommandOption("--check-only")]
    [Description("Only the duration estimate and the environment checks (-CheckOnly).")]
    public bool CheckOnly { get; init; }

    public override ValidationResult Validate() =>
        Mode is null || Enum.TryParse<FullMode>(Mode, ignoreCase: true, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error($"Unknown mode '{Mode}'. Use full, quick or dry.");

    public FullOptions ToOptions() => new()
    {
        Mode = Mode is null ? FullMode.Full : Enum.Parse<FullMode>(Mode, ignoreCase: true),
        Local = Local,
        ResultsDir = string.IsNullOrWhiteSpace(ResultsDir) ? null : ResultsDir.Trim(),
        SkipNet8 = SkipNet8,
        LowMemory = LowMemory,
        Force = Force,
        CheckOnly = CheckOnly,
    };
}

/// <summary><c>full ...</c>: run-all.ps1 with the given flags, at once (no confirmation; --check-only for the checks).</summary>
internal sealed class FullCommand : AsyncCommand<FullSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, FullSettings settings, CancellationToken cancellationToken) =>
        FullRunner.RunAsync(BenchPaths.Current, settings.ToOptions(), printBenchCommand: false);
}

/// <summary>Runs run-all.ps1 (checks only, or the run with progress) and shows the outcome.</summary>
internal static class FullRunner
{
    public static async Task<int> RunAsync(BenchPaths paths, FullOptions options, bool printBenchCommand)
    {
        if (Tools.CheckPrerequisites() is { } missing)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return 1;
        }
        if (options.OverwritesPublishedResults)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: a {options.Mode} run writes its reports into results/, over the committed ones; " +
                "pass --results-dir (e.g. ../artifacts/bench-dry) to keep them.[/]");
        }
        var command = new ScriptCommand(paths.RunAllScript, options.ToScriptArguments(), paths.BenchmarksDir);
        ScriptRunner.PrintCommand(command, paths);

        int exitCode = options.CheckOnly
            ? (await CheckAsync(command).ConfigureAwait(false)).ExitCode
            : await RunWithProgressAsync(command, paths).ConfigureAwait(false);

        if (printBenchCommand)
        {
            AnsiConsole.MarkupLine("[grey]The same with the CLI, without the menu:[/]");
            Console.WriteLine("  " + options.ToBenchCommand());
        }
        return exitCode;
    }

    /// <summary>run-all.ps1 -CheckOnly: the estimate and the checks; the script's error when one failed.</summary>
    public static async Task<(int ExitCode, IReadOnlyList<PreflightCheck> Checks, RunAllPlan? Plan)> CheckAsync(ScriptCommand command)
    {
        var lines = new List<string>();
        using var interrupts = new InterruptHandler(confirm: false);
        Task<ScriptResult> run = ScriptRunner.RunAsync(command, line => { lock (lines) lines.Add(line); }, _ => { }, interrupts);
        ScriptResult result = Terminal.Plain
            ? await run.ConfigureAwait(false)
            : await AnsiConsole.Status().StartAsync("Environment checks...", _ => run).ConfigureAwait(false);

        var events = lines.Select(RunAllOutput.Parse).ToList();
        var checks = events.OfType<RunAllPreflight>().Select(e => e.Check).ToList();
        RunAllPlan? plan = events.OfType<RunAllPlan>().FirstOrDefault();
        string? mode = events.OfType<RunAllMode>().FirstOrDefault()?.Mode;
        string? results = events.OfType<RunAllResultsDir>().FirstOrDefault()?.Dir;
        if (plan is not null)
        {
            AnsiConsole.MarkupLine($"{Markup.Escape(mode ?? "?")} run into {Markup.Escape(results ?? "?")}: ~{plan.TotalMinutes} min on the reference machine " +
                $"[grey]({Markup.Escape(string.Join(", ", plan.Steps.Select(s => $"{s.Step} {s.Minutes}")))})[/]");
        }
        CheckView.Write(checks);
        if (result.Interrupted)
            AnsiConsole.MarkupLine("[yellow]Stopped.[/]");
        else if (result.ExitCode != 0)
            AnsiConsole.MarkupLine($"[red]Environment check failed (exit code {result.ExitCode}):[/] {Markup.Escape(result.ErrorMessage)}");
        else
            AnsiConsole.MarkupLine("[green]Checks passed.[/]");
        return (result.ExitCode, checks, plan);
    }

    private static async Task<int> RunWithProgressAsync(ScriptCommand command, BenchPaths paths)
    {
        var monitor = new RunAllMonitor(paths.BenchmarksDir);
        ScriptResult result = await RunSession.RunAsync(command, monitor, keepAwake: true).ConfigureAwait(false);
        if (result.Interrupted)
            return result.ExitCode;

        if (result.ExitCode != 0)
        {
            if (!Terminal.Plain && monitor.Checks.Any(c => c.Mark == CheckMark.Fail))
                CheckView.Write(monitor.Checks);
            AnsiConsole.MarkupLine($"[red]run-all.ps1 failed (exit code {result.ExitCode}):[/] {Markup.Escape(result.ErrorMessage)}");
            if (monitor.ResultsDirFullPath is { } dir)
                AnsiConsole.MarkupLine($"[grey]Logs: {Markup.Escape(Path.Combine(dir, "raw", "run-all"))}[/]");
            return result.ExitCode;
        }

        string? report = monitor.ResultsFile is { } file ? Path.GetFullPath(file) : monitor.ResultsDirFullPath is { } results ? Path.Combine(results, ResultsRun.FileName) : null;
        if (report is not null && File.Exists(report))
        {
            AnsiConsole.WriteLine();
            ResultsView.WriteResultsRun(ResultsRun.Load(paths.RelativeToBenchmarks(Path.GetDirectoryName(report)!), report));
        }
        if (monitor.ResultsDirFullPath is { } resultsDir)
        {
            AnsiConsole.MarkupLine($"[grey]Raw data and logs: {Markup.Escape(Path.Combine(resultsDir, "raw"))} " +
                $"(run-info.json, run-all/*.log)[/]");
        }
        AnsiConsole.MarkupLine("[green]Done.[/]");
        return 0;
    }
}
