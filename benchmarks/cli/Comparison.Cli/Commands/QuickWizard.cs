using Comparison.Cli.Artifacts;
using Comparison.Cli.Parsing;
using Comparison.Cli.Processes;
using Spectre.Console;

namespace Comparison.Cli.Commands;

/// <summary>
/// The interactive quick A/B: context, choices preselected with the script's defaults, -CheckOnly, confirmation, run.
/// A choice equal to the script's default is not passed.
/// </summary>
internal static class QuickWizard
{
    private const string NormalMode = "Normal: default job, --launchCount 3, as the full run (~75 s per case and variant)";
    private const string DryMode = "Dry: one iteration (--job Dry), checks the commands; the numbers mean nothing";

    public static async Task<int> RunAsync(BenchPaths paths)
    {
        if (Tools.CheckPrerequisites() is { } missing)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return 1;
        }

        ScriptDefaults defaults = ScriptDefaults.Read(paths.RunQuickScript);
        string? defaultVersion = defaults.String("PublishedVersion");
        GitState git = BenchEnvironment.ReadGit(paths.RepoDir);
        WriteContext(paths, git, defaultVersion);

        // Benchmark classes.
        IReadOnlyList<string> classes = BenchmarkCatalog.Discover(paths.BenchmarkSourcesDir);
        IReadOnlyList<string> defaultClasses = (defaults.Strings("Filter") ?? [])
            .Select(BenchmarkCatalog.ToName).Where(classes.Contains).ToList();
        var classPrompt = new MultiSelectionPrompt<string>()
            .Title($"Benchmark classes [grey](script default: {Markup.Escape(string.Join(", ", defaultClasses.DefaultIfEmpty("?")))})[/]")
            .InstructionsText("[grey](space: select, enter: accept)[/]")
            .AddChoices(classes);
        foreach (string name in defaultClasses)
            classPrompt.Select(name);
        List<string> selectedClasses = AnsiConsole.Prompt(classPrompt);

        // Frameworks: only those with an installed runtime can be chosen.
        IReadOnlySet<int> runtimes = BenchEnvironment.RuntimeMajors();
        IReadOnlyList<string> suite = BenchEnvironment.SuiteFrameworks(paths.BenchmarksDir);
        var available = suite.Where(f => BenchEnvironment.Major(f) is { } major && runtimes.Contains(major)).ToList();
        foreach (string framework in suite.Except(available))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(framework)}: not available (no Microsoft.NETCore.App {BenchEnvironment.Major(framework)}.x runtime)[/]");
        if (available.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]None of the suite's frameworks has an installed runtime.[/]");
            return 1;
        }
        IReadOnlyList<string> defaultFrameworks = defaults.Strings("Frameworks") ?? [];
        var frameworkPrompt = new MultiSelectionPrompt<string>()
            .Title($"Frameworks [grey](script default: {Markup.Escape(defaults.Describe("Frameworks"))})[/]")
            .InstructionsText("[grey](space: select, enter: accept)[/]")
            .AddChoices(available);
        foreach (string framework in defaultFrameworks.Intersect(available))
            frameworkPrompt.Select(framework);
        List<string> selectedFrameworks = AnsiConsole.Prompt(frameworkPrompt);

        string version = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Published version [grey](empty: script default, {Markup.Escape(defaultVersion ?? "unknown")})[/]")
            .AllowEmpty()).Trim();
        if (version.Length > 0 && version != defaultVersion && !git.TagExists(paths.RepoDir, "v" + version))
            AnsiConsole.MarkupLine($"[yellow]Tag v{Markup.Escape(version)} not found locally (git fetch --tags); the pre-flight will fail.[/]");

        bool dry = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Mode").AddChoices(NormalMode, DryMode)) == DryMode;

        var options = new QuickOptions
        {
            Benchmarks = SameSet(selectedClasses, defaultClasses) ? null : selectedClasses,
            Frameworks = SameSet(selectedFrameworks, defaultFrameworks) ? null : Ordered(selectedFrameworks, defaultFrameworks.Concat(suite)),
            PublishedVersion = version.Length == 0 || version == defaultVersion ? null : version,
            Dry = dry,
        };

        // Checks first; nothing is measured unless they pass.
        AnsiConsole.WriteLine();
        var check = new ScriptCommand(paths.RunQuickScript, (options with { CheckOnly = true }).ToScriptArguments(), paths.BenchmarksDir);
        ScriptRunner.PrintCommand(check, paths);
        (int checkCode, IReadOnlyList<PreflightCheck> checks, string? runs) = await QuickRunner.CheckAsync(check).ConfigureAwait(false);
        if (checkCode != 0 || checks.Any(c => c.Mark == CheckMark.Fail))
        {
            AnsiConsole.MarkupLine("[red]Not started: fix the failed checks first.[/]");
            AnsiConsole.MarkupLine("[grey]The same check with the CLI:[/]");
            Console.WriteLine("  " + (options with { CheckOnly = true }).ToBenchCommand());
            return checkCode != 0 ? checkCode : 1;
        }

        if (runs is not null)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(runs)}[/]");
        if (!AnsiConsole.Prompt(new ConfirmationPrompt("Start the run?")))
        {
            AnsiConsole.MarkupLine("[grey]Not started. The same run with the CLI:[/]");
            Console.WriteLine("  " + options.ToBenchCommand());
            return 0;
        }
        AnsiConsole.WriteLine();
        return await QuickRunner.RunAsync(paths, options, printBenchCommand: true).ConfigureAwait(false);
    }

    private static void WriteContext(BenchPaths paths, GitState git, string? version)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        grid.AddRow("[grey]Branch[/]", Markup.Escape(git.Branch ?? "?"));
        grid.AddRow("[grey]HEAD[/]", Markup.Escape(git.Head ?? "?") + (git.Uncommitted ? " [yellow](uncommitted changes)[/]" : " [green](clean)[/]"));
        if (version is null)
            grid.AddRow("[grey]Published[/]", "[yellow]script default unknown[/]");
        else
        {
            bool tag = git.TagExists(paths.RepoDir, "v" + version);
            grid.AddRow("[grey]Published[/]", $"{Markup.Escape(version)} " + (tag ? $"[green](tag v{Markup.Escape(version)} found)[/]" : $"[red](tag v{Markup.Escape(version)} not found: git fetch --tags)[/]"));
        }
        AnsiConsole.Write(new Panel(grid).Header("Quick A/B: published package vs local sources (run-quick.ps1)").Expand());
    }

    private static bool SameSet(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == b.Count && a.All(b.Contains);

    private static IReadOnlyList<string> Ordered(IReadOnlyList<string> selected, IEnumerable<string> order) =>
        order.Distinct().Where(selected.Contains).Concat(selected).Distinct().ToList();
}
