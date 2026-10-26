using System.ComponentModel;
using Comparison.Cli.Artifacts;
using Comparison.Cli.Processes;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Comparison.Cli.Commands;

internal class ResultsBranchSettings : CommandSettings;

internal sealed class ResultsShowSettings : ResultsBranchSettings
{
    [CommandArgument(0, "[folder]")]
    [Description("A quick run (stamp such as 20261024-113550, or its path) or a full run folder (results/2.0.0). Without it: the list.")]
    public string? Folder { get; init; }
}

internal sealed class SummarizeSettings : ResultsBranchSettings
{
    [CommandArgument(0, "<folder>")]
    [Description("The quick run folder (stamp such as 20261024-113550, or its path).")]
    public string Folder { get; init; } = "";
}

/// <summary><c>results [folder]</c>: lists the runs, or shows one.</summary>
internal sealed class ResultsCommand : Command<ResultsShowSettings>
{
    private static readonly string[] Subcommands = ["show", "summarize"];

    /// <summary>
    /// <c>results &lt;folder&gt;</c> → <c>results show &lt;folder&gt;</c>: Spectre.Console.Cli reads a word after a branch
    /// as a subcommand, not as an argument of the branch's default command.
    /// </summary>
    public static string[] RewriteShortForm(string[] args) =>
        args.Length >= 2 && args[0] == "results" && !args[1].StartsWith('-') && !Subcommands.Contains(args[1])
            ? ["results", "show", .. args[1..]]
            : args;

    public override int Execute(CommandContext context, ResultsShowSettings settings, CancellationToken cancellationToken)
    {
        BenchPaths paths = BenchPaths.Current;
        if (string.IsNullOrWhiteSpace(settings.Folder))
        {
            ResultsView.WriteList(paths);
            return 0;
        }
        string? dir = RunFolders.Resolve(settings.Folder, paths);
        if (dir is null)
        {
            AnsiConsole.MarkupLine($"[red]Folder not found:[/] {Markup.Escape(settings.Folder)} (looked as a path, in " +
                $"{Markup.Escape(BenchPaths.Display(paths.QuickArtifactsDir))}, benchmarks/ and the repository).");
            return 1;
        }
        if (RunFolders.IsResultsRun(dir))
        {
            ResultsView.WriteResultsRun(ResultsRun.Load(paths.RelativeToBenchmarks(dir), Path.Combine(dir, ResultsRun.FileName)));
            return 0;
        }
        if (RunFolders.IsQuickRun(dir))
        {
            ResultsView.WriteQuickRun(QuickRun.Load(dir));
            return 0;
        }
        AnsiConsole.MarkupLine($"[red]Not a run folder:[/] {Markup.Escape(dir)} (no run-info.json, no RESULTS.md).");
        return 1;
    }
}

/// <summary><c>results summarize &lt;folder&gt;</c>: run-quick.ps1 -SummarizeOnly &lt;folder&gt; writes summary.md again.</summary>
internal sealed class SummarizeCommand : AsyncCommand<SummarizeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SummarizeSettings settings, CancellationToken cancellationToken)
    {
        BenchPaths paths = BenchPaths.Current;
        string? dir = RunFolders.Resolve(settings.Folder, paths);
        if (dir is null || !RunFolders.IsQuickRun(dir))
        {
            AnsiConsole.MarkupLine($"[red]Not a quick run folder:[/] {Markup.Escape(settings.Folder)}");
            return 1;
        }
        return await RunAsync(paths, dir).ConfigureAwait(false);
    }

    public static async Task<int> RunAsync(BenchPaths paths, string runDir)
    {
        if (Tools.CheckPrerequisites() is { } missing)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return 1;
        }
        var command = new ScriptCommand(paths.RunQuickScript, ["-SummarizeOnly", paths.RelativeToBenchmarks(runDir)], paths.BenchmarksDir);
        ScriptRunner.PrintCommand(command, paths);

        // Plain: the script's output as it is. Otherwise its copy of summary.md is not echoed; the tables are shown
        // from the file afterwards.
        using var interrupts = new InterruptHandler(confirm: false);
        ScriptResult result = await ScriptRunner.RunAsync(
            command,
            line => { if (Terminal.Plain) { Console.WriteLine(line); } },
            ScriptRunner.WriteStderr,
            interrupts).ConfigureAwait(false);

        if (result.Interrupted)
        {
            AnsiConsole.MarkupLine("[yellow]Stopped.[/]");
            return result.ExitCode;
        }
        if (result.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]run-quick.ps1 -SummarizeOnly failed (exit code {result.ExitCode}).[/]");
            return result.ExitCode;
        }
        if (!Terminal.Plain)
        {
            ResultsView.WriteQuickRun(QuickRun.Load(runDir));
        }

        return 0;
    }
}
