using Comparison.Cli.Commands;
using Spectre.Console;

namespace Comparison.Cli.Tests;

/// <summary>The terminal (non-plain) views, rendered into a string.</summary>
[Collection(nameof(ConsoleCollection))]
public class ResultsViewTests
{
    [Fact]
    public void QuickRun_ShowsStatusesAndTheSummaryTable()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string run = Path.Combine(root.Path, "20261024-113550");
        Directory.CreateDirectory(Path.Combine(run, "published-net10.0"));
        File.Copy(Fixture.PathOf("run-info.json"), Path.Combine(run, "run-info.json"));
        File.Copy(Fixture.PathOf("summary.md"), Path.Combine(run, "summary.md"));
        File.Copy(Fixture.PathOf("assemblies.published.json"), Path.Combine(run, "published-net10.0", "assemblies.json"));

        string output = Render(() => ResultsView.WriteQuickRun(Cli.Artifacts.QuickRun.Load(run)));

        Assert.Contains("Quick A/B 20261024-113550", output);
        Assert.Contains("published net10.0: ok", output);
        Assert.Contains("local net10.0: not run", output);
        Assert.Contains("Flat.IronMapper_IMapper", output); // shortened for the terminal: FlatBenchmark.IronMapper_IMapper
        Assert.Contains("Δ Allocated", output);
    }

    [Fact]
    public void List_ShowsQuickAndFullRuns()
    {
        using Fixture.TempDir bench = Fixture.NewTempDir();
        string benchmarks = Path.Combine(bench.Path, "benchmarks");
        string run = Path.Combine(bench.Path, "artifacts", "quick", "20261024-113550");
        Directory.CreateDirectory(run);
        File.Copy(Fixture.PathOf("run-info.json"), Path.Combine(run, "run-info.json"));
        Directory.CreateDirectory(Path.Combine(benchmarks, "results", "2.0.0"));
        File.Copy(Fixture.PathOf("RESULTS.2.0.0.head.md"), Path.Combine(benchmarks, "results", "2.0.0", "RESULTS.md"));

        string output = Render(() => ResultsView.WriteList(BenchPaths.ForTests(benchmarks)));

        Assert.Contains("20261024-113550", output);
        Assert.Contains("Dry", output);
        Assert.Contains("results/2.0.0", output);
        Assert.Contains("909da54", output);
    }

    [Fact]
    public void WideTable_IsSplit_RepeatingTheFirstColumn()
    {
        // The widths of summary.md's columns for Collection (+3 per column): it does not fit 120 columns.
        int[] widths = [36, 17, 14, 17, 14, 11, 18, 18, 14];
        IReadOnlyList<IReadOnlyList<int>> groups = Cli.Ui.MarkdownView.SplitColumns(widths, 120);
        Assert.Equal([[0, 1, 2, 3, 4, 5], [0, 6, 7, 8]], groups.Select(g => g.ToArray()));
        Assert.All(groups, g => Assert.True(g.Sum(i => widths[i]) + 1 <= 120));

        // Everything fits: one table; a column wider than the terminal still gets a table of its own.
        Assert.Single(Cli.Ui.MarkdownView.SplitColumns(widths, 200));
        Assert.Equal([[0, 1], [0, 2]], Cli.Ui.MarkdownView.SplitColumns([30, 100, 100], 120).Select(g => g.ToArray()));
    }

    private static string Render(Action write)
    {
        IAnsiConsole previous = AnsiConsole.Console;
        var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        AnsiConsole.Console.Profile.Width = 250;
        try
        {
            write();
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
        return writer.ToString();
    }
}

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public class ConsoleCollection;
