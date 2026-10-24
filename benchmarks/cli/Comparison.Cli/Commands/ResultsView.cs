using Comparison.Cli.Artifacts;
using Comparison.Cli.Ui;
using Spectre.Console;

namespace Comparison.Cli.Commands;

/// <summary>Read-only views of finished runs: artifacts/quick/* and results/*/RESULTS.md.</summary>
internal static class ResultsView
{
    public static void WriteList(BenchPaths paths)
    {
        if (Terminal.Plain)
        {
            WritePlainList(paths);
            return;
        }
        IReadOnlyList<QuickRun> quick = QuickRun.List(paths.QuickArtifactsDir);
        AnsiConsole.MarkupLine($"[bold]Quick A/B runs[/] [grey]({Markup.Escape(BenchPaths.Display(paths.QuickArtifactsDir))})[/]");
        if (quick.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey]none yet[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded)
                .AddColumns("Run", "Time", "Branch", "Commit", "Filter", "Frameworks", "Mode", "Status");
            foreach (QuickRun run in quick)
            {
                table.AddRow(
                    Markup.Escape(run.Stamp),
                    run.Time?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    Markup.Escape(run.Branch ?? "?"),
                    Markup.Escape(run.ShortCommit + (run.Uncommitted ? " +changes" : "")),
                    Markup.Escape(string.Join(", ", run.Filter.Select(BenchmarkCatalog.ToName))),
                    Markup.Escape(string.Join(", ", run.Frameworks)),
                    run.InfoProblem is not null ? "[red]?[/]" : run.Dry ? "[yellow]Dry[/]" : "Normal",
                    run.InfoProblem is not null ? $"[red]{Markup.Escape(run.InfoProblem)}[/]" : StatusCell(run));
            }
            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        IReadOnlyList<ResultsRun> full = ResultsRun.List(paths.ResultsDir);
        AnsiConsole.MarkupLine($"[bold]Full runs[/] [grey]({Markup.Escape(BenchPaths.Display(paths.ResultsDir))}/*/RESULTS.md)[/]");
        if (full.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey]none[/]");
            return;
        }
        var results = new Table().Border(TableBorder.Rounded).AddColumns("Folder", "Run", "Commit", "Note");
        foreach (ResultsRun run in full)
        {
            results.AddRow(
                Markup.Escape(run.Name),
                Markup.Escape(ShortRun(run.Run) ?? "?"),
                Markup.Escape(run.Commit ?? ""),
                run.Marker is null ? "" : $"[yellow]{Markup.Escape(run.Marker)}[/]");
        }
        AnsiConsole.Write(results);
        AnsiConsole.MarkupLine("[grey]Details: results <folder> (e.g. results " +
            $"{Markup.Escape(quick.FirstOrDefault()?.Stamp ?? "results/2.0.0")}).[/]");
    }

    /// <summary>One line per run, for files, pipes and CI logs.</summary>
    private static void WritePlainList(BenchPaths paths)
    {
        Console.WriteLine($"Quick A/B runs ({BenchPaths.Display(paths.QuickArtifactsDir)}):");
        IReadOnlyList<QuickRun> quick = QuickRun.List(paths.QuickArtifactsDir);
        if (quick.Count == 0)
        {
            Console.WriteLine("  none yet");
        }

        foreach (QuickRun run in quick)
        {
            string info = run.InfoProblem is not null
                ? run.InfoProblem
                : $"{run.Branch} {run.ShortCommit}{(run.Uncommitted ? " +changes" : "")} · " +
                  $"{string.Join(",", run.Filter.Select(BenchmarkCatalog.ToName))} · {string.Join(",", run.Frameworks)} · {(run.Dry ? "Dry" : "Normal")}";
            string statuses = string.Join("; ", run.Runs.Select(r => $"{r.Variant} {r.Framework}: {r.Status.Text}"));
            Console.WriteLine($"  {run.Stamp}  {info} · {(statuses.Length > 0 ? statuses : "no runs")}");
        }
        Console.WriteLine();
        Console.WriteLine($"Full runs ({BenchPaths.Display(paths.ResultsDir)}/*/RESULTS.md):");
        IReadOnlyList<ResultsRun> full = ResultsRun.List(paths.ResultsDir);
        if (full.Count == 0)
        {
            Console.WriteLine("  none");
        }

        foreach (ResultsRun run in full)
        {
            Console.WriteLine($"  {run.Name}  {ShortRun(run.Run) ?? "?"} · {run.Commit}{(run.Marker is null ? "" : $" · {run.Marker}")}");
        }
    }

    public static void WriteQuickRun(QuickRun run)
    {
        if (Terminal.Plain && run.HasSummary)
        {
            // Line by line: the file as the script wrote it, plus where it is.
            Console.WriteLine(File.ReadAllText(run.SummaryPath).TrimEnd());
            Console.WriteLine();
            Console.WriteLine($"Summary: {run.SummaryPath}");
            return;
        }

        AnsiConsole.Write(new Rule($"[bold]Quick A/B {Markup.Escape(run.Stamp)}[/]").LeftJustified());
        if (run.InfoProblem is not null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(run.InfoProblem)}[/]");
        }

        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        grid.AddRow("[grey]Branch, commit[/]", Markup.Escape($"{run.Branch ?? "?"} {run.ShortCommit}{(run.Uncommitted ? " + uncommitted changes" : "")}"));
        grid.AddRow("[grey]Published[/]", Markup.Escape(run.PublishedVersion ?? "?"));
        grid.AddRow("[grey]Filter[/]", Markup.Escape(string.Join(" ", run.Filter)));
        grid.AddRow("[grey]Job[/]", Markup.Escape(run.Job ?? "?") + (run.Dry ? " [yellow](Dry)[/]" : ""));
        grid.AddRow("[grey]Runs[/]", StatusCell(run));
        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();

        if (run.HasSummary)
        {
            MarkdownDocument document = MarkdownDocument.Parse(File.ReadAllText(run.SummaryPath));
            // The header bullets repeat the grid above; show the framework sections.
            foreach (MarkdownBlock block in document.Blocks.SkipWhile(b => b is not MarkdownHeading { Level: 2 }))
            {
                WriteBlock(block);
            }

            AnsiConsole.MarkupLine($"[grey]Summary: {Markup.Escape(run.SummaryPath)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No summary.md[/] (the run did not finish). Rebuild it: " +
                $"[cyan]results summarize {Markup.Escape(run.Stamp)}[/]");
        }
        AnsiConsole.MarkupLine($"[grey]Folder: {Markup.Escape(run.Dir)}[/]");
    }

    public static void WriteResultsRun(ResultsRun run)
    {
        string markdown = File.ReadAllText(run.Path);
        if (Terminal.Plain)
        {
            Console.WriteLine($"{run.Name}: {run.Run}");
            Console.WriteLine($"Commit: {run.Commit}");
            MarkdownTable? plainSummary = MarkdownDocument.Parse(markdown).TableAfter("Summary");
            if (plainSummary is not null)
            {
                Console.WriteLine("| " + string.Join(" | ", plainSummary.Headers) + " |");
                foreach (IReadOnlyList<string> row in plainSummary.Rows)
                {
                    Console.WriteLine("| " + string.Join(" | ", row) + " |");
                }
            }
            Console.WriteLine($"Report: {run.Path}");
            return;
        }

        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(run.Name)}[/]").LeftJustified());
        if (run.Marker is not null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(run.Marker)}: the numbers are not for publication.[/]");
        }

        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        grid.AddRow("[grey]Run[/]", Markup.Escape(run.Run ?? "?"));
        grid.AddRow("[grey]Commit[/]", Markup.Escape(run.Commit ?? "?"));
        AnsiConsole.Write(grid);
        MarkdownTable? summary = MarkdownDocument.Parse(markdown).TableAfter("Summary");
        if (summary is not null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(MarkdownView.ToTable(summary));
        }
        AnsiConsole.MarkupLine($"[grey]Report: {Markup.Escape(run.Path)}[/]");
    }

    private static void WriteBlock(MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                AnsiConsole.MarkupLine($"[bold underline]{Markup.Escape(heading.Text)}[/]");
                break;
            case MarkdownTable table:
                AnsiConsole.Write(MarkdownView.ToTable(table, ShortBenchmarkName));
                AnsiConsole.WriteLine();
                break;
            case MarkdownText text:
                foreach (string line in text.Lines)
                {
                    AnsiConsole.MarkupLine(Markup.Escape(MarkdownView.Inline(line)));
                }

                break;
        }
    }

    /// <summary>"CollectionBenchmark.Mapperly(Count=1000)" → "Collection.Mapperly(Count=1000)", for the terminal only.</summary>
    private static string ShortBenchmarkName(string name) => name.Replace("Benchmark.", ".", StringComparison.Ordinal);

    public static string StatusCell(QuickRun run) =>
        run.Runs.Count == 0
            ? "[grey]no runs[/]"
            : string.Join("\n", run.Runs.Select(r => $"{Markup.Escape($"{r.Variant} {r.Framework}")}: {Status(r.Status)}"));

    public static string Status(RunStatus status)
    {
        string color = status.State switch
        {
            RunState.Ok => "green",
            RunState.Failed or RunState.Invalid => "red",
            _ => "yellow",
        };
        return $"[{color}]{Markup.Escape(status.Text)}[/]";
    }

    /// <summary>The "Run" cell up to the step list: "Full, started ..., total 2:41:07".</summary>
    private static string? ShortRun(string? run)
    {
        if (run is null)
        {
            return null;
        }

        int paren = run.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? run[..paren] : run;
    }
}
