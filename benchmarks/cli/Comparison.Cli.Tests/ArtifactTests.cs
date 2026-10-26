using Comparison.Cli.Artifacts;

namespace Comparison.Cli.Tests;

public class ArtifactTests
{
    [Fact]
    public void AssembliesJson_OfARealRun_IsOk()
    {
        // Its ChildBuilds is an object, not an array (ConvertTo-Json unwraps one element); only ExitCode and Problems count.
        RunStatus status = RunStatus.FromJson(Fixture.Text("assemblies.published.json"), Path.Combine("x", "run"));
        Assert.Equal(RunState.Ok, status.State);
        Assert.Equal("ok", status.Text);
    }

    [Theory]
    [InlineData("""{"ExitCode": 1, "Problems": []}""", "Failed", @"FAILED (exit code 1, log: RUNDIR\run.log)")]
    [InlineData("""{"ExitCode": 0, "Problems": ["wrong version", "no child build"]}""", "Invalid", "INVALID: wrong version; no child build")]
    [InlineData("""{"ExitCode": 0, "Problems": "the child build of BenchmarkDotNet was not found"}""", "Invalid", "INVALID: the child build of BenchmarkDotNet was not found")]
    [InlineData("""{"ExitCode": 0}""", "Ok", "ok")]
    [InlineData("""{"ExitCode": 0, "Problems": null, "Host": 42, "SomethingNew": {"a": [1]}}""", "Ok", "ok")]
    [InlineData("""{"Host": {}}""", "Unknown", "unknown (no ExitCode in assemblies.json)")]
    [InlineData("not json", "Unknown", null)]
    public void AssembliesJson_FollowsGetRunStatus(string json, string state, string? text)
    {
        string dir = Path.Combine("x", "published-net10.0");
        RunStatus status = RunStatus.FromJson(json, dir);
        Assert.Equal(Enum.Parse<RunState>(state), status.State);
        if (text is not null)
        {
            Assert.Equal(text.Replace("RUNDIR", dir).Replace('\\', Path.DirectorySeparatorChar), status.Text.Replace('\\', Path.DirectorySeparatorChar));
        }
    }

    [Fact]
    public void MissingAssembliesJson_IsNotRun()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        Assert.Equal(new RunStatus(RunState.NotRun, "not run"), RunStatus.Read(dir.Path));
    }

    [Theory]
    [InlineData("ok", "Ok")]
    [InlineData("FAILED (exit code 1, log: x)", "Failed")]
    [InlineData("INVALID: x", "Invalid")]
    [InlineData("not run", "NotRun")]
    [InlineData("something new", "Unknown")]
    public void StatusText_IsClassified(string text, string state) => Assert.Equal(Enum.Parse<RunState>(state), RunStatus.Classify(text));

    [Fact]
    public void QuickRunFolder_IsRead()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string run = Path.Combine(root.Path, "20261024-113550");
        Directory.CreateDirectory(Path.Combine(run, "published-net10.0"));
        Directory.CreateDirectory(Path.Combine(run, "local-net10.0"));
        File.Copy(Fixture.PathOf("run-info.json"), Path.Combine(run, "run-info.json"));
        File.Copy(Fixture.PathOf("summary.md"), Path.Combine(run, "summary.md"));
        File.Copy(Fixture.PathOf("assemblies.published.json"), Path.Combine(run, "published-net10.0", "assemblies.json"));
        File.WriteAllText(Path.Combine(run, "local-net10.0", "assemblies.json"), """{"ExitCode": 3, "Problems": []}""");
        Directory.CreateDirectory(Path.Combine(root.Path, "not-a-stamp"));

        QuickRun loaded = Assert.Single(QuickRun.List(root.Path));
        Assert.Equal("20261024-113550", loaded.Stamp);
        Assert.Equal(new DateTime(2026, 10, 24, 11, 35, 50), loaded.Time);
        Assert.Equal("main", loaded.Branch);
        Assert.Equal("253e90c", loaded.ShortCommit);
        Assert.False(loaded.Uncommitted);
        Assert.Equal("2.0.0", loaded.PublishedVersion);
        Assert.Equal(["*.FlatBenchmark.*"], loaded.Filter);
        Assert.Equal(["net10.0"], loaded.Frameworks);
        Assert.True(loaded.Dry);
        Assert.True(loaded.HasSummary);
        Assert.Null(loaded.InfoProblem);
        Assert.Equal(["published net10.0 Ok", "local net10.0 Failed"], loaded.Runs.Select(r => $"{r.Variant} {r.Framework} {r.Status.State}"));
    }

    [Fact]
    public void QuickRunFolder_WithoutRunInfo_FindsTheVariantFolders()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string run = Path.Combine(root.Path, "20261023-090000");
        Directory.CreateDirectory(Path.Combine(run, "published-net8.0"));
        File.WriteAllText(Path.Combine(run, "run-info.json"), """{"Filter": "*.NestedBenchmark.*", "Frameworks": "net8.0"}""");
        Directory.CreateDirectory(Path.Combine(root.Path, "20261023-100000", "local-net10.0"));

        IReadOnlyList<QuickRun> runs = QuickRun.List(root.Path);
        Assert.Equal(["20261023-100000", "20261023-090000"], runs.Select(r => r.Stamp));
        Assert.Equal("no run-info.json", runs[0].InfoProblem);
        Assert.Equal(["net10.0"], runs[0].Frameworks);
        // One-element arrays written as strings by ConvertTo-Json.
        Assert.Equal(["*.NestedBenchmark.*"], runs[1].Filter);
        Assert.Equal(["published", "local"], runs[1].Runs.Select(r => r.Variant));
        Assert.All(runs[1].Runs, r => Assert.Equal(RunState.NotRun, r.Status.State));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void SummaryMarkdown_GivesTheFrameworkTable(string newline)
    {
        MarkdownDocument document = MarkdownDocument.Parse(Fixture.Text("summary.md", newline));
        MarkdownTable table = document.TableAfter("net10.0")!;
        Assert.Equal(9, table.Headers.Count);
        Assert.Equal("Benchmark", table.Headers[0]);
        Assert.Equal("Δ Allocated", table.Headers[^1]);
        Assert.Equal(ColumnAlignment.Left, table.Alignments[0]);
        Assert.All(table.Alignments.Skip(1), a => Assert.Equal(ColumnAlignment.Right, a));
        Assert.Equal(6, table.Rows.Count);
        Assert.Equal(["FlatBenchmark.IronMapper", "1526200.0 ns", "1.11"], table.Rows[1].Take(3));
        Assert.Contains(document.Blocks, b => b is MarkdownText t && t.Lines.Contains("Published: ok. Local: ok."));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void ResultsMarkdown_GivesRunCommitAndSummary(string newline)
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string dir = Path.Combine(root.Path, "2.0.0");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "RESULTS.md"), Fixture.Text("RESULTS.2.0.0.head.md", newline));
        File.WriteAllText(Path.Combine(root.Path, "RESULTS.md"), "# x\n\n> **Dry run** (`run-all.ps1 -Dry`)\n\n| | |\n|---|---|\n| Run | Dry, started x |\n");

        IReadOnlyList<ResultsRun> runs = ResultsRun.List(root.Path);
        Assert.Equal(["results", "results/2.0.0"], runs.Select(r => r.Name));
        Assert.Equal("Dry run", runs[0].Marker);
        Assert.Equal("Dry, started x", runs[0].Run);
        Assert.Null(runs[1].Marker);
        Assert.Equal("909da54", runs[1].Commit);
        Assert.StartsWith("Full, started 2026-10-21 19:17 UTC, total 2:29:58 (Build 0:00:11", runs[1].Run);

        MarkdownTable summary = MarkdownDocument.Parse(Fixture.Text("RESULTS.2.0.0.head.md", newline)).TableAfter("Summary")!;
        Assert.Equal("Library", summary.Headers[0]);
        Assert.Equal(["Manual", "IronMapper", "IronMapper_IMapper", "Mapperly", "Mapster", "AutoMapper"], summary.Rows.Select(r => r[0]));
    }

    [Fact]
    public void BenchmarkClasses_AreTheRuntimeClassesOfRunAll()
    {
        string bench = Fixture.BenchmarksDir;
        IReadOnlyList<string> discovered = BenchmarkCatalog.Discover(Path.Combine(bench, "src", "Comparison.Benchmarks"));

        // $runtimeClasses = 'Flat', 'Nested', ... in run-all.ps1.
        string line = File.ReadLines(Path.Combine(bench, "scripts", "run-all.ps1")).First(l => l.StartsWith("$runtimeClasses = ", StringComparison.Ordinal));
        var fromScript = line["$runtimeClasses = ".Length..].Split(',').Select(s => s.Trim().Trim('\'')).Order(StringComparer.Ordinal);
        Assert.Equal(fromScript, discovered);
        Assert.DoesNotContain("Startup", discovered);
    }

    [Fact]
    public void BenchmarkNames_MapToTheScriptFilters()
    {
        IReadOnlyList<string> known = ["Collection", "Flat", "InPlace", "Nested"];
        IReadOnlyList<string> filters = BenchmarkCatalog.ToFilters(["nested", "InPlaceBenchmark", "Nested", "Bogus"], known, out IReadOnlyList<string> unknown);
        Assert.Equal(["*.NestedBenchmark.*", "*.InPlaceBenchmark.*"], filters);
        Assert.Equal(["Bogus"], unknown);
        Assert.Equal("Nested", BenchmarkCatalog.ToName("*.NestedBenchmark.*"));
        Assert.Equal("*Collection*", BenchmarkCatalog.ToName("*Collection*"));
    }
}
