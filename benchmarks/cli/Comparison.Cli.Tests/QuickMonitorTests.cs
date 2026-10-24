using Comparison.Cli.Monitoring;

namespace Comparison.Cli.Tests;

public class QuickMonitorTests
{
    [Fact]
    public void RealDryRun_StagesProgressAndSummary()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        string runDir = Path.Combine(root.Path, "published-net10.0");
        Directory.CreateDirectory(runDir);
        var monitor = new QuickMonitor();

        string[] stdout = Fixture.Lines("run-quick.dry.stdout.txt")
            .Select(l => l.StartsWith("Output: ", StringComparison.Ordinal) ? "Output: " + root.Path : l)
            .ToArray();
        foreach (string line in stdout)
        {
            monitor.OnStdout(line);

            // The BenchmarkDotNet log of the published run appears while it runs.
            if (line.EndsWith("published on net10.0: run", StringComparison.Ordinal))
            {
                // File.Copy keeps the old write time, which LogTail rightly treats as a log of an earlier run.
                File.Copy(Fixture.PathOf("bdn-flat-dry.run.log"), Path.Combine(runDir, "run.log"));
                File.SetLastWriteTimeUtc(Path.Combine(runDir, "run.log"), DateTime.UtcNow);
                monitor.Poll(DateTime.UtcNow);
                Assert.Contains("6/6", monitor.PlainUpdates(DateTime.UtcNow.AddMinutes(1)).Single());
            }
        }

        Assert.Equal(root.Path, monitor.Root);
        Assert.Equal(["net10.0"], monitor.Header!.Frameworks);
        Assert.Equal(10, monitor.Checks.Count);
        Assert.EndsWith("summary.md", monitor.SummaryPath);
        Assert.Equal([root.Path], monitor.PartialArtifacts);

        // No hang warning right after a finished run.
        Assert.Empty(monitor.PlainUpdates(DateTime.UtcNow));
    }

    [Fact]
    public void SilentLog_GivesAHangWarning_OnlyAfterTheThreshold()
    {
        using Fixture.TempDir root = Fixture.NewTempDir();
        Directory.CreateDirectory(Path.Combine(root.Path, "published-net8.0"));
        var monitor = new QuickMonitor();
        monitor.OnStdout("Filter: *.CollectionBenchmark.*; frameworks: net8.0; job: default job, --launchCount 3 (as the full run)");
        monitor.OnStdout("Output: " + root.Path);
        monitor.OnStdout("[03:00:00] published on net8.0: build");
        monitor.OnStdout("[03:00:10] published on net8.0: run");

        DateTime now = DateTime.UtcNow;
        Assert.DoesNotContain(monitor.PlainUpdates(now.Add(Phase.HangThreshold).AddSeconds(-30)), l => l.Contains("warning"));
        string warning = Assert.Single(monitor.PlainUpdates(now.Add(Phase.HangThreshold).AddSeconds(30)), l => l.Contains("warning"));
        Assert.Contains("no log output for 5 min", warning);
        Assert.Contains(Path.Combine(root.Path, "published-net8.0", "run.log"), warning);
    }
}
