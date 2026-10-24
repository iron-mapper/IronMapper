using Comparison.Cli.Parsing;
using Comparison.Cli.Processes;
using Spectre.Console;

namespace Comparison.Cli.Commands;

/// <summary>The interactive full run (run-all.ps1): choices, -CheckOnly, confirmation, run.</summary>
internal static class FullWizard
{
    private const string FullChoice = "Full: the published methodology (--launchCount 3, every scenario); the checks print the estimate";
    private const string QuickChoice = "Quick: --job short, fewer runs, smaller N; checks the pipeline, numbers not for publication";
    private const string DryChoice = "Dry: one iteration, 2 startup runs, N = 5; checks that every command works";
    private const string PackageSource = "The published package (Directory.Packages.props)";
    private const string LocalSource = "The local sources (../src)";

    public static async Task<int> RunAsync(BenchPaths paths)
    {
        if (Tools.CheckPrerequisites() is { } missing)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return 1;
        }
        GitState git = BenchEnvironment.ReadGit(paths.RepoDir);
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        grid.AddRow("[grey]Branch[/]", Markup.Escape(git.Branch ?? "?"));
        grid.AddRow("[grey]HEAD[/]", Markup.Escape(git.Head ?? "?") + (git.Uncommitted ? " [yellow](uncommitted changes)[/]" : " [green](clean)[/]"));
        AnsiConsole.Write(new Panel(grid).Header("Full run: every scenario, then RESULTS.md (run-all.ps1)").Expand());

        string modeChoice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Mode").AddChoices(FullChoice, QuickChoice, DryChoice));
        FullMode mode = modeChoice == QuickChoice ? FullMode.Quick : modeChoice == DryChoice ? FullMode.Dry : FullMode.Full;
        bool local = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("IronMapper").AddChoices(PackageSource, LocalSource)) == LocalSource;

        string results;
        if (DefaultResultsDir(mode, DateTime.Now) is { } own)
        {
            // Quick and Dry never go to results/ unless the user types it: results/ holds the committed release reports.
            AnsiConsole.MarkupLine("[grey]Quick and Dry numbers are not for publication; they get their own folder (passed as -ResultsDir).[/]");
            results = AnsiConsole.Prompt(new TextPrompt<string>("Results folder, relative to benchmarks/").DefaultValue(own)).Trim();
            if (IsPublishedResults(results))
            {
                AnsiConsole.MarkupLine("[yellow]This replaces the committed reports in results/ with numbers not for publication.[/]");
            }
        }
        else
        {
            results = AnsiConsole.Prompt(new TextPrompt<string>("Results folder, relative to benchmarks/ [grey](empty: script default, results/)[/]").AllowEmpty()).Trim();
        }

        bool net8Runtime = BenchEnvironment.RuntimeMajors().Contains(8);
        if (!net8Runtime)
        {
            AnsiConsole.MarkupLine("[yellow]No .NET 8 runtime: the net8.0 runtime benchmarks cannot run.[/]");
        }

        bool skipNet8 = AnsiConsole.Prompt(new ConfirmationPrompt("Skip the runtime benchmarks on net8.0?") { DefaultValue = !net8Runtime });
        bool lowMemory = AnsiConsole.Prompt(new ConfirmationPrompt("NativeAOT with a single ILC thread (less memory, slower)?") { DefaultValue = false });

        var options = new FullOptions
        {
            Mode = mode,
            Local = local,
            ResultsDir = results.Length == 0 ? null : results,
            SkipNet8 = skipNet8,
            LowMemory = lowMemory,
        };

        // Checks first. A failed memory check can be overridden with -Force for Quick and Dry only; anything else stops here.
        while (true)
        {
            AnsiConsole.WriteLine();
            var check = new ScriptCommand(paths.RunAllScript, (options with { CheckOnly = true }).ToScriptArguments(), paths.BenchmarksDir);
            ScriptRunner.PrintCommand(check, paths);
            (int code, IReadOnlyList<PreflightCheck> checks, _) = await FullRunner.CheckAsync(check).ConfigureAwait(false);
            var failed = checks.Where(c => c.Mark == CheckMark.Fail).ToList();
            if (code == 0 && failed.Count == 0)
            {
                break;
            }

            bool onlyMemory = failed.Count > 0 && failed.All(c => c.Name == "Free memory");
            if (onlyMemory && !options.Force && MayOfferForce(mode) &&
                AnsiConsole.Prompt(new ConfirmationPrompt("Too little free memory for the NativeAOT publish. Continue anyway (-Force)?") { DefaultValue = false }))
            {
                options = options with { Force = true };
                continue;
            }
            if (onlyMemory && !MayOfferForce(mode))
            {
                AnsiConsole.MarkupLine("[yellow]A full run with too little free memory gives distorted numbers or a failed NativeAOT publish. " +
                    "Close other applications and check again. " +
                    $"To run anyway, pass --force to {Markup.Escape(BenchCommand.Wrapper)} full yourself.[/]");
            }
            AnsiConsole.MarkupLine("[red]Not started: fix the failed checks first.[/]");
            AnsiConsole.MarkupLine("[grey]The same check with the CLI:[/]");
            Console.WriteLine("  " + (options with { CheckOnly = true }).ToBenchCommand());
            return code != 0 ? code : 1;
        }

        if (!AnsiConsole.Prompt(new ConfirmationPrompt("Start the run?")))
        {
            AnsiConsole.MarkupLine("[grey]Not started. The same run with the CLI:[/]");
            Console.WriteLine("  " + options.ToBenchCommand());
            return 0;
        }
        AnsiConsole.WriteLine();
        return await FullRunner.RunAsync(paths, options, printBenchCommand: true).ConfigureAwait(false);
    }

    /// <summary>
    /// The folder the menu proposes: ../artifacts/bench-quick-&lt;yyyyMMdd-HHmmss&gt; (or bench-dry-...) for Quick and Dry;
    /// null for Full, which writes to the script's default, results/.
    /// </summary>
    internal static string? DefaultResultsDir(FullMode mode, DateTime now) =>
        mode == FullMode.Full ? null : $"../artifacts/bench-{mode.ToString().ToLowerInvariant()}-{now:yyyyMMdd-HHmmss}";

    /// <summary>-Force for too little memory is offered for Quick and Dry; a full run's numbers go into the report.</summary>
    internal static bool MayOfferForce(FullMode mode) => mode != FullMode.Full;

    /// <summary>results/ itself (the committed reports of the latest release), however it is written.</summary>
    internal static bool IsPublishedResults(string dir) =>
        dir.Replace('\\', '/').Trim().TrimEnd('/') is "results" or "./results";
}
