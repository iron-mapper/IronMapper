using Comparison.Cli.Parsing;

namespace Comparison.Cli.Tests;

public class ScriptOutputTests
{
    [Fact]
    public void QuickDryStdout_GivesRootChecksStagesAndSummary()
    {
        var events = Fixture.Lines("run-quick.dry.stdout.txt").Select(QuickOutput.Parse).Where(e => e is not null).ToList();

        Assert.Equal(@"C:\Users\algmironov\source\repos\IronMapper\artifacts\quick\20261024-113550", Assert.Single(events.OfType<QuickOutputRoot>()).Path);
        var checks = events.OfType<QuickPreflight>().Select(e => e.Check).ToList();
        Assert.Equal(10, checks.Count);
        Assert.Equal(CheckMark.Warn, Assert.Single(checks, c => c.Name == "Free memory").Mark);
        PreflightCheck license = Assert.Single(checks, c => c.Name == "AutoMapper license");
        Assert.Equal(@"C:\Users\algmironov\.automapper\license.txt exists", license.Detail);

        var stages = events.OfType<QuickStage>().ToList();
        Assert.Equal(
            ["published net10.0 Build", "published net10.0 Run", "published net10.0 Result:ok", "local net10.0 Build", "local net10.0 Run", "local net10.0 Result:ok"],
            stages.Select(s => $"{s.Variant} {s.Framework} {s.Kind}{(s.Kind == QuickStageKind.Result ? ":" + s.Text : "")}"));
        Assert.Equal(new TimeOnly(11, 35, 59), stages[0].Time);

        Assert.Single(events.OfType<QuickSummaryEcho>());
        Assert.EndsWith(@"20261024-113550\summary.md", Assert.Single(events.OfType<QuickSummaryWritten>()).Path);
    }

    [Fact]
    public void QuickStageStatuses_KeepTheScriptText()
    {
        var failed = (QuickStage)QuickOutput.Parse(@"[01:02:03] local on net8.0: FAILED (exit code 1, log: C:\x\run.log)")!;
        Assert.Equal(QuickStageKind.Result, failed.Kind);
        Assert.Equal(@"FAILED (exit code 1, log: C:\x\run.log)", failed.Text);
        var invalid = (QuickStage)QuickOutput.Parse("[01:02:03] published on net10.0: INVALID: the child build of BenchmarkDotNet was not found")!;
        Assert.StartsWith("INVALID:", invalid.Text);
    }

    [Fact]
    public void QuickCheckOnlyFail_IsAFailLineAndAThrowOnStderr()
    {
        var checks = Fixture.Lines("run-quick.checkonly-fail.stdout.txt").Select(PreflightCheck.TryParse).OfType<PreflightCheck>().ToList();
        PreflightCheck branch = Assert.Single(checks, c => c.Mark == CheckMark.Fail);
        Assert.Equal("Branch", branch.Name);
        Assert.Equal("main (expected no-such-branch)", branch.Detail);

        Assert.Equal("Pre-flight failed:   Branch: main (expected no-such-branch)", ScriptError.Extract(Fixture.Lines("run-quick.checkonly-fail.stderr.txt")));
    }

    [Fact]
    public void RunAllCheckOnly_GivesModeResultsPlanAndChecks()
    {
        var events = Fixture.Lines("run-all.checkonly.stdout.txt").Select(RunAllOutput.Parse).Where(e => e is not null).ToList();

        Assert.Equal("Full", Assert.Single(events.OfType<RunAllMode>()).Mode);
        Assert.Equal("results", Assert.Single(events.OfType<RunAllResultsDir>()).Dir);
        RunAllPlan plan = Assert.Single(events.OfType<RunAllPlan>());
        Assert.Equal(163, plan.TotalMinutes);
        Assert.Equal(["Build", "Tests", "Runtime", "RuntimeNet8", "Startup", "AOT", "Footprint", "BuildTime", "Report"], plan.Steps.Select(s => s.Step));
        Assert.Equal(70, plan.Steps.Single(s => s.Step == "Runtime").Minutes);

        var checks = events.OfType<RunAllPreflight>().Select(e => e.Check).ToList();
        Assert.Equal(4, checks.Count);
        Assert.Equal("Free memory", Assert.Single(checks, c => c.Mark == CheckMark.Fail).Name);

        Assert.Equal("Environment check failed:   Free memory: 3.0 GB free, 4 GB needed for the NativeAOT publish",
            ScriptError.Extract(Fixture.Lines("run-all.checkonly.stderr.txt")));
    }

    // The step lines of run-all.ps1 (Invoke-Step, Invoke-Dotnet, the last line), as its format strings write them;
    // the dash may arrive mangled when pwsh's console code page is not UTF-8.
    [Theory]
    [InlineData("==> Runtime (~70 min) — 22:50")]
    [InlineData("==> Runtime (~70 min) ? 22:50")]
    [InlineData("==> Runtime (~70 min) вЂ” 22:50")]
    public void RunAllStepStart_DoesNotDependOnTheDash(string line)
    {
        var started = Assert.IsType<RunAllStepStarted>(RunAllOutput.Parse(line));
        Assert.Equal("Runtime", started.Step);
        Assert.Equal(70, started.EstimateMinutes);
    }

    [Fact]
    public void RunAllStepLines_AreRecognized()
    {
        Assert.Equal(new TimeSpan(1, 10, 35), Assert.IsType<RunAllStepDone>(RunAllOutput.Parse("    done in 1:10:35")).Elapsed);
        Assert.Equal(@"C:\b\results\raw\run-all\bdn-runtime.log", Assert.IsType<RunAllLog>(RunAllOutput.Parse(@"    log: C:\b\results\raw\run-all\bdn-runtime.log")).Path);
        RunAllDone done = Assert.IsType<RunAllDone>(RunAllOutput.Parse(@"All done in 2:29:58. Results: C:\b\results\RESULTS.md"));
        Assert.Equal(new TimeSpan(2, 29, 58), done.Elapsed);
        Assert.Equal(@"C:\b\results\RESULTS.md", done.ResultsFile);
        Assert.Equal("results/2.0.0", Assert.IsType<RunAllResultsDir>(RunAllOutput.Parse("IronMapper: local sources (../src); results: results/2.0.0")).Dir);
        Assert.Equal("Dry", Assert.IsType<RunAllMode>(RunAllOutput.Parse("IronMapper comparison benchmarks ? Dry run")).Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Close other applications and keep the machine on mains power: background load changes the numbers.")]
    [InlineData("Written: C:\\b\\results\\startup.md")]
    [InlineData("WARNING: something")]
    public void UnknownLines_AreIgnored(string line)
    {
        Assert.Null(QuickOutput.Parse(line));
        Assert.Null(RunAllOutput.Parse(line));
    }

    [Fact]
    public void ScriptError_WithoutConciseView_IsTheStderrText()
    {
        Assert.Equal("boom" + Environment.NewLine + "second", ScriptError.Extract(["", "boom", "  ", "second  "]));
    }
}
