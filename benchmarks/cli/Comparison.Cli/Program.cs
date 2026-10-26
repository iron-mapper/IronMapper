using System.Text;
using Comparison.Cli;
using Comparison.Cli.Commands;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

args = Terminal.Configure(args);

// Box drawing and "·" need UTF-8. In a terminal the console's code page is switched and restored on exit; redirected
// output (a file, a pipe) is written as UTF-8 without touching the console's code page.
Encoding? originalEncoding = null;
if (Console.IsOutputRedirected)
{
    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
}
else if (Console.OutputEncoding.CodePage != Encoding.UTF8.CodePage)
{
    originalEncoding = Console.OutputEncoding;
    Console.OutputEncoding = new UTF8Encoding(false);
}
if (Console.IsErrorRedirected)
{
    Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
}
// A file or a CI log has no width; Spectre would wrap at 80 columns.
if (Console.IsOutputRedirected)
{
    AnsiConsole.Profile.Width = 240;
}

try
{
    try
    {
        BenchPaths.Current = BenchPaths.Locate();
    }
    catch (InvalidOperationException ex)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        return 1;
    }

    if (args.Length == 0)
    {
        if (Terminal.Interactive)
        {
            return await Menu.RunAsync(BenchPaths.Current);
        }

        args = ["--help"];
    }

    args = ResultsCommand.RewriteShortForm(args);
    var app = new CommandApp();
    app.Configure(config =>
    {
        config.SetApplicationName(BenchCommand.Wrapper);
        config.AddCommand<QuickCommand>("quick")
            .WithDescription("Quick A/B: the published IronMapper package against the local sources (scripts/run-quick.ps1). Runs at once; --check-only for the checks.")
            .WithExample("quick", "--filter", "'Nested,InPlace'", "--frameworks", "'net10.0,net8.0'", "--dry")
            .WithExample("quick", "--check-only");
        config.AddCommand<FullCommand>("full")
            .WithDescription("Full run: every scenario, then RESULTS.md (scripts/run-all.ps1). Runs at once; --check-only for the estimate and the checks.")
            .WithExample("full", "--check-only")
            .WithExample("full", "--mode", "dry", "--results-dir", "../artifacts/bench-dry")
            .WithExample("full", "--local", "--results-dir", "results/2.1.0");
        config.AddBranch<ResultsBranchSettings>("results", results =>
        {
            results.SetDescription("List finished runs (artifacts/quick, results/*), show one, or rebuild a quick run's summary.");
            results.SetDefaultCommand<ResultsCommand>();
            results.AddCommand<ResultsCommand>("show")
                .WithDescription("Show one run: a quick run's statuses and summary.md, or a full run's summary table. Short form: results <folder>.")
                .WithExample("results", "show", "20261024-113550")
                .WithExample("results", "results/2.0.0");
            results.AddCommand<SummarizeCommand>("summarize")
                .WithDescription("Write summary.md of a quick run again (run-quick.ps1 -SummarizeOnly).")
                .WithExample("results", "summarize", "20261024-113550");
        });
        config.SetExceptionHandler((ex, _) =>
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        });
    });
    return await app.RunAsync(args);
}
finally
{
    if (originalEncoding is not null)
    {
        Console.OutputEncoding = originalEncoding;
    }
}
