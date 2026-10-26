using Comparison.Cli.Artifacts;
using Spectre.Console;

namespace Comparison.Cli.Commands;

/// <summary>The interactive menu (no arguments, in a terminal).</summary>
internal static class Menu
{
    private const string QuickItem = "Quick A/B: published package vs local sources (run-quick.ps1)";
    private const string FullItem = "Full run: every scenario, then RESULTS.md (run-all.ps1)";
    private const string ResultsItem = "Results";
    private const string ExitItem = "Exit";

    /// <summary>A run ends the CLI with the script's exit code; Results returns to the menu.</summary>
    public static async Task<int> RunAsync(BenchPaths paths)
    {
        while (true)
        {
            string choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]IronMapper benchmarks[/]")
                .AddChoices(QuickItem, FullItem, ResultsItem, ExitItem));
            switch (choice)
            {
                case QuickItem:
                    return await QuickWizard.RunAsync(paths).ConfigureAwait(false);
                case FullItem:
                    return await FullWizard.RunAsync(paths).ConfigureAwait(false);
                case ResultsItem:
                    await BrowseResultsAsync(paths).ConfigureAwait(false);
                    break;
                default:
                    return 0;
            }
        }
    }

    /// <summary>The list of runs; a quick run can be opened and its summary rebuilt.</summary>
    private static async Task BrowseResultsAsync(BenchPaths paths)
    {
        ResultsView.WriteList(paths);
        IReadOnlyList<QuickRun> quick = QuickRun.List(paths.QuickArtifactsDir);
        IReadOnlyList<ResultsRun> full = ResultsRun.List(paths.ResultsDir);
        const string back = "Back";
        var choices = quick.Select(r => $"quick {r.Stamp}").Concat(full.Select(r => r.Name)).Append(back).ToList();
        string selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Open a run").PageSize(15).AddChoices(choices));
        if (selected == back)
        {
            return;
        }

        ResultsRun? fullRun = full.FirstOrDefault(r => r.Name == selected);
        if (fullRun is not null)
        {
            ResultsView.WriteResultsRun(fullRun);
            return;
        }
        QuickRun run = quick.First(r => $"quick {r.Stamp}" == selected);
        ResultsView.WriteQuickRun(run);
        const string rebuild = "Rebuild summary (run-quick.ps1 -SummarizeOnly)";
        if (AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices(back, rebuild)) == rebuild)
        {
            await SummarizeCommand.RunAsync(paths, run.Dir).ConfigureAwait(false);
        }
    }
}
