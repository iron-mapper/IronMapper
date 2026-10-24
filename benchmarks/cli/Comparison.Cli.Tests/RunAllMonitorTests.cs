using Comparison.Cli.Monitoring;
using Comparison.Cli.Parsing;

namespace Comparison.Cli.Tests;

public class RunAllMonitorTests
{
    [Fact]
    public void CheckOnlyFail_GivesPlanAndChecks_NoSteps()
    {
        var monitor = new RunAllMonitor(Fixture.BenchmarksDir);
        foreach (string line in Fixture.Lines("run-all.checkonly.stdout.txt"))
            monitor.OnStdout(line);

        Assert.Equal("Full", monitor.Mode);
        Assert.Equal("results", monitor.ResultsDir);
        Assert.Equal(Path.Combine(Fixture.BenchmarksDir, "results"), monitor.ResultsDirFullPath);
        Assert.Equal(9, monitor.Plan!.Steps.Count);
        Assert.Equal(CheckMark.Fail, monitor.Checks.Single(c => c.Name == "Free memory").Mark);
        Assert.False(monitor.AllDone);
    }

    [Fact]
    public void RealDryRun_StepsLogsAndEnd()
    {
        string[] stdout = Fixture.Lines("run-all.dry.stdout.txt");
        using Fixture.TempDir root = Fixture.NewTempDir();
        var monitor = new RunAllMonitor(root.Path);
        var logs = new List<string>();
        foreach (string line in stdout)
        {
            monitor.OnStdout(line);
            if (RunAllOutput.Parse(line) is RunAllLog log)
                logs.Add(Path.GetFileName(log.Path));
        }
        monitor.Complete(0, interrupted: false);

        Assert.Equal("Dry", monitor.Mode);
        Assert.True(monitor.AllDone);
        Assert.EndsWith("RESULTS.md", monitor.ResultsFile);
        // The steps of the plan, all started and finished.
        var started = stdout.Select(RunAllOutput.Parse).OfType<RunAllStepStarted>().Select(s => s.Step).ToList();
        Assert.Equal(monitor.Plan!.Steps.Select(s => s.Step), started);
        Assert.Equal(started.Count, stdout.Select(RunAllOutput.Parse).OfType<RunAllStepDone>().Count());
        // Invoke-Dotnet's "log:" lines (the line added for the CLI).
        Assert.Contains("build.log", logs);
        Assert.Contains("tests.log", logs);
        Assert.Contains("bdn-runtime.log", logs);
        Assert.Contains("bdn-runtime-net8.0.log", logs);
    }

    [Fact]
    public void StepThatThrows_IsFailed_AndRuntimeFollowsItsBdnLog()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string raw = Path.Combine(root.Path, "results", "raw", "run-all");
        Directory.CreateDirectory(raw);
        var monitor = new RunAllMonitor(root.Path);
        monitor.OnStdout("IronMapper comparison benchmarks — Quick run");
        monitor.OnStdout("IronMapper: package of Directory.Packages.props; results: results");
        monitor.OnStdout("Expected duration on the reference machine: ~25 min (Build 1, Tests 1, Runtime 8, Startup 3)");
        monitor.OnStdout("==> Build (~1 min) — 10:00");
        monitor.OnStdout($"    log: {Path.Combine(raw, "build.log")}");
        monitor.OnStdout("    done in 0:00:40");
        monitor.OnStdout("==> Runtime (~8 min) — 10:01");
        string bdnLog = Path.Combine(raw, "bdn-runtime.log");
        monitor.OnStdout($"    log: {bdnLog}");
        File.Copy(Fixture.PathOf("bdn-two-classes-dry.run.log"), bdnLog);
        File.SetLastWriteTimeUtc(bdnLog, DateTime.UtcNow);
        monitor.Poll(DateTime.UtcNow);

        string update = Assert.Single(monitor.PlainUpdates(DateTime.UtcNow));
        Assert.Contains("Runtime: 12/12", update);
        Assert.Equal([Path.Combine(root.Path, "results", "raw"), bdnLog], monitor.PartialArtifacts);

        monitor.Complete(1, interrupted: false);
        Assert.Empty(monitor.PlainUpdates(DateTime.UtcNow.AddHours(1)));
    }
}

public class FullWizardRulesTests
{
    [Fact]
    public void QuickAndDry_GetTheirOwnFolder_FullKeepsTheScriptDefault()
    {
        var now = new DateTime(2026, 10, 24, 15, 4, 5);
        Assert.Equal("../artifacts/bench-quick-20261024-150405", Cli.Commands.FullWizard.DefaultResultsDir(Cli.Commands.FullMode.Quick, now));
        Assert.Equal("../artifacts/bench-dry-20261024-150405", Cli.Commands.FullWizard.DefaultResultsDir(Cli.Commands.FullMode.Dry, now));
        Assert.Null(Cli.Commands.FullWizard.DefaultResultsDir(Cli.Commands.FullMode.Full, now));

        // The proposed folder is passed explicitly, so it shows in the printed command.
        var options = new Cli.Commands.FullOptions { Mode = Cli.Commands.FullMode.Dry, ResultsDir = "../artifacts/bench-dry-20261024-150405" };
        Assert.Equal(["-Dry", "-ResultsDir", "../artifacts/bench-dry-20261024-150405"], options.ToScriptArguments());
        Assert.False(options.OverwritesPublishedResults);
    }

    [Fact]
    public void ForceIsOffered_ForQuickAndDryOnly()
    {
        Assert.False(Cli.Commands.FullWizard.MayOfferForce(Cli.Commands.FullMode.Full));
        Assert.True(Cli.Commands.FullWizard.MayOfferForce(Cli.Commands.FullMode.Quick));
        Assert.True(Cli.Commands.FullWizard.MayOfferForce(Cli.Commands.FullMode.Dry));
    }

    [Theory]
    [InlineData("results", true)]
    [InlineData("results/", true)]
    [InlineData(@".\results\", true)]
    [InlineData("results/2.1.0", false)]
    [InlineData("../artifacts/bench-dry-1", false)]
    public void PublishedResultsFolder_IsRecognized(string dir, bool published) =>
        Assert.Equal(published, Cli.Commands.FullWizard.IsPublishedResults(dir));
}
