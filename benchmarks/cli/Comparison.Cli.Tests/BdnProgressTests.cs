using Comparison.Cli.Parsing;

namespace Comparison.Cli.Tests;

public class BdnProgressTests
{
    [Fact]
    public void FlatDryLog_CountsAllCases()
    {
        var progress = new BdnProgress();
        foreach (string line in Fixture.Lines("bdn-flat-dry.run.log"))
            progress.Feed(line);

        Assert.True(progress.Recognized);
        Assert.True(progress.Finished);
        Assert.Equal(6, progress.Total);
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(6, progress.Completed);
        Assert.Equal(TimeSpan.Zero, progress.Eta);
        Assert.Equal("6/6 · finished", progress.Describe());
    }

    [Fact]
    public void FlatDryLog_InTheMiddle()
    {
        var progress = new BdnProgress();
        foreach (string line in Fixture.Lines("bdn-flat-dry.run.log"))
        {
            progress.Feed(line);
            if (line.StartsWith("// Benchmark: FlatBenchmark.IronMapper:", StringComparison.Ordinal))
                break;
        }

        Assert.Equal(1, progress.Completed);
        Assert.Equal("FlatBenchmark.IronMapper", progress.CurrentBenchmark);
        Assert.False(progress.Finished);
        Assert.Equal("1/6 · FlatBenchmark.IronMapper · ETA 0h 0m", progress.Describe());
    }

    [Fact]
    public void TwoClasses_TheTotalIsTheRunsNotTheClasss()
    {
        var progress = new BdnProgress();
        var completedWhenSecondClassStarts = -1;
        foreach (string line in Fixture.Lines("bdn-two-classes-dry.run.log"))
        {
            progress.Feed(line);
            if (line.StartsWith("// Benchmark: InPlaceBenchmark.Manual:", StringComparison.Ordinal))
                completedWhenSecondClassStarts = progress.Completed;
        }

        // "// Found 6 benchmarks:" appears once per class and must not replace the 12 of the whole run.
        Assert.Equal(12, progress.Total);
        Assert.Equal(6, completedWhenSecondClassStarts);
        Assert.Equal(12, progress.Completed);
        Assert.True(progress.Finished);
    }

    [Fact]
    public void LaunchesParametersAndEta_OfAFullJob()
    {
        // The line formats of BenchmarkDotNet 0.15.8 with --launchCount 3 and a [Params] benchmark.
        var progress = new BdnProgress();
        progress.Feed("// ***** Found 53 benchmark(s) in total *****");
        progress.Feed("// Benchmark: CollectionBenchmark.IronMapper: DefaultJob(LaunchCount=3) [Count=100000]");
        progress.Feed("// Launch: 2 / 3");
        progress.Feed("// ** Remained 46 (86,8 %) benchmark(s) to run. Estimated finish 2026-10-24 3:05 (1h 12m from now) **");

        Assert.Equal(7, progress.Completed);
        Assert.Equal("CollectionBenchmark.IronMapper [Count=100000]", progress.CurrentBenchmark);
        Assert.Equal(new TimeSpan(1, 12, 0), progress.Eta);
        Assert.Equal("7/53 · CollectionBenchmark.IronMapper [Count=100000] · launch 2/3 · ETA 1h 12m", progress.Describe());

        // A new case resets the launch.
        progress.Feed("// Benchmark: CollectionBenchmark.Mapperly: DefaultJob(LaunchCount=3) [Count=100000]");
        Assert.Null(progress.Launch);
    }

    [Theory]
    [InlineData("// ** Remained 5 (83.3%) benchmark(s) to run. Estimated finish 10/24/2026 11:36 AM (0h 3m from now) **")]
    [InlineData("// ** Remained 5 (83,3 %) benchmark(s) to run. Estimated finish 2026-10-24 11:36 (0h 3m from now) **")]
    public void Remained_DoesNotReadTheCultureDependentParts(string line)
    {
        var progress = new BdnProgress();
        progress.Feed("// ***** Found 6 benchmark(s) in total *****");
        progress.Feed(line);
        Assert.Equal(1, progress.Completed);
        Assert.Equal(TimeSpan.FromMinutes(3), progress.Eta);
    }

    [Fact]
    public void UnknownLines_ChangeNothing()
    {
        var progress = new BdnProgress();
        Assert.False(progress.Feed("Setup power plan (GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c FriendlyName: Высокая производительность)"));
        Assert.False(progress.Feed("// start dotnet  restore --nodeReuse:false"));
        Assert.False(progress.Feed("WorkloadActual   1: 1 op, 1526000.00 ns, 1.5260 ms/op"));
        Assert.False(progress.Recognized);
        Assert.Equal("", progress.Describe());
    }
}
