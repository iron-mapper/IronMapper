using System.Diagnostics;
using Comparison.Cli.Processes;

namespace Comparison.Cli.Tests;

public class ProcessTests
{
    [Theory]
    [InlineData("-Dry", "-Dry")]
    [InlineData("net10.0,net8.0", "'net10.0,net8.0'")]
    [InlineData("*.NestedBenchmark.*,*.InPlaceBenchmark.*", "'*.NestedBenchmark.*,*.InPlaceBenchmark.*'")]
    [InlineData("../artifacts/quick/20261024-113550", "../artifacts/quick/20261024-113550")]
    [InlineData("results/my run", "'results/my run'")]
    [InlineData(@"..\artifacts\x", @"'..\artifacts\x'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("it's $x", "'it''s $x'")]
    public void Arguments_AreQuotedForCopying(string arg, string shown) => Assert.Equal(shown, ScriptCommand.Quote(arg));

    [Fact]
    public void AnsiColours_OfPwshErrorView_AreRemoved()
    {
        // stderr of run-quick.ps1 -CheckOnly -Branch nope, read from pwsh in its hidden console.
        string[] colored =
        [
            "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m               ~~~~~~~~~~~~~~~~~~~~~~~~\u001b[0m",
            "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mPre-flight failed:   Branch: feature/bench-cli (expected nope)\u001b[0m",
        ];
        string[] plain = colored.Select(ScriptProcess.StripAnsi).ToArray();
        Assert.Equal("     |                ~~~~~~~~~~~~~~~~~~~~~~~~", plain[0]);
        Assert.Equal("Pre-flight failed:   Branch: feature/bench-cli (expected nope)", Cli.Parsing.ScriptError.Extract(plain));
    }

    [Fact]
    public void Display_IsTheExactPwshCommand()
    {
        string bench = Path.Combine(Path.GetTempPath(), "b");
        var command = new ScriptCommand(Path.Combine(bench, "scripts", "run-quick.ps1"), ["-Filter", "*.FlatBenchmark.*", "-Frameworks", "net10.0", "-Dry"], bench);
        Assert.Equal("pwsh -NoProfile -NonInteractive -File ./scripts/run-quick.ps1 -Filter '*.FlatBenchmark.*' -Frameworks net10.0 -Dry", command.Display);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-File", Path.Combine(bench, "scripts", "run-quick.ps1"), "-Filter", "*.FlatBenchmark.*", "-Frameworks", "net10.0", "-Dry"], command.PwshArguments);
    }

    [Fact]
    public async Task Stdout_IsDecodedRight_WithCyrillicFromARealLog()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        // The power plan line of a real BenchmarkDotNet log and the dash of run-all.ps1's header, printed by pwsh to
        // its redirected stdout in its own (hidden) console's code page.
        string script = Path.Combine(dir.Path, "echo.ps1");
        File.WriteAllText(script, $"""
            Get-Content -LiteralPath '{Fixture.PathOf("bdn-flat-dry.run.log")}' -Encoding utf8 | Where-Object {"{"} $_ -like 'Setup power plan*' {"}"} | Select-Object -First 1 | Write-Host
            Write-Host "IronMapper comparison benchmarks $([char]0x2014) Dry run"
            Write-Error 'на stderr'
            exit 3
            """, new System.Text.UTF8Encoding(true));
        string expected = Fixture.Lines("bdn-flat-dry.run.log").First(l => l.StartsWith("Setup power plan", StringComparison.Ordinal));

        var stdout = new List<string>();
        using var interrupts = new InterruptHandler(confirm: false);
        ScriptResult result = await ScriptRunner.RunAsync(new ScriptCommand(script, [], dir.Path), stdout.Add, _ => { }, interrupts);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Interrupted);
        Assert.Equal(expected, stdout[0]);
        Assert.Contains("Высокая производительность", stdout[0]);
        // UTF-8 end to end (chcp 65001 on Windows): no character is lost on the way.
        Assert.Equal("IronMapper comparison benchmarks — Dry run", stdout[1]);
        Assert.Contains("на stderr", result.ErrorMessage);
    }

    [Fact]
    public async Task DotnetOutputRedirectedByTheScript_IsUtf8InTheLog()
    {
        // What the scripts do: dotnet ... *> $log. MSBuild writes UTF-8; with the OEM code page of a hidden console
        // pwsh decoded it as 866 and the log got "╤Д╨░╨╣╨╗ ╨┐╤А╨╛╨╡╨║╤В╨░". DOTNET_CLI_UI_LANGUAGE is set in this test only, so
        // MSBuild prints Russian on any machine.
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string script = Path.Combine(dir.Path, "log.ps1");
        File.WriteAllText(script, """
            $env:DOTNET_CLI_UI_LANGUAGE = 'ru'
            & dotnet build (Join-Path $PSScriptRoot 'missing.csproj') -nologo *> (Join-Path $PSScriptRoot 'build.log')
            Write-Host "done"
            """);

        using var interrupts = new InterruptHandler(confirm: false);
        ScriptResult result = await ScriptRunner.RunAsync(new ScriptCommand(script, [], dir.Path), _ => { }, _ => { }, interrupts);

        Assert.Equal(0, result.ExitCode);
        string log = File.ReadAllText(Path.Combine(dir.Path, "build.log"), System.Text.Encoding.UTF8);
        Assert.Contains("MSB1009", log);
        Assert.Contains("файл проекта не существует", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("╨", log);
    }

    [Fact]
    public async Task Arguments_ReachTheScriptUnchanged()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string folder = Path.Combine(dir.Path, "a folder & (more)");
        Directory.CreateDirectory(folder);
        string script = Path.Combine(folder, "args.ps1");
        File.WriteAllText(script, "param([string[]] $Filter, [string] $ResultsDir, [switch] $Dry)\nforeach ($a in @($Filter) + $ResultsDir + \"dry=$Dry\") { Write-Host \"[$a]\" }");
        string[] args = ["-Filter", "*.FlatBenchmark.*,*.NestedBenchmark.*", "-ResultsDir", @"results\run 1 (x) & y^z\", "-Dry"];

        var stdout = new List<string>();
        using var interrupts = new InterruptHandler(confirm: false);
        ScriptResult result = await ScriptRunner.RunAsync(new ScriptCommand(script, args, dir.Path), stdout.Add, _ => { }, interrupts);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["[*.FlatBenchmark.*,*.NestedBenchmark.*]", @"[results\run 1 (x) & y^z\]", "[dry=True]"], stdout);
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("100%")]
    public void ArgumentsCmdCannotCarry_AreRefused(string arg)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(() => ScriptProcess.CmdQuote(arg));
    }

    [Fact]
    public async Task Stop_KillsTheWholeTree()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        // pwsh starts a second pwsh (as run-all starts dotnet and BenchmarkDotNet its own processes) and waits.
        string script = Path.Combine(dir.Path, "tree.ps1");
        File.WriteAllText(script, """
            $child = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'Start-Sleep 300' -PassThru -NoNewWindow
            Write-Host "child $($child.Id)"
            Start-Sleep 300
            """);
        var childPid = new TaskCompletionSource<int>();
        using var interrupts = new InterruptHandler(confirm: false);
        Task<ScriptResult> run = ScriptRunner.RunAsync(new ScriptCommand(script, [], dir.Path), line =>
        {
            if (line.StartsWith("child ", StringComparison.Ordinal))
            {
                childPid.TrySetResult(int.Parse(line[6..]));
            }
        }, _ => { }, interrupts);

        int pid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        Assert.False(Process.GetProcessById(pid).HasExited);

        interrupts.OnCtrlC();
        ScriptResult result = await run.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.True(result.Interrupted);
        Assert.Equal(ScriptRunner.InterruptedExitCode, result.ExitCode);
        Assert.True(HasExited(pid), $"child process {pid} still runs");
    }

    [Fact]
    public void CtrlC_WithConfirmation_AsksFirst_ThenStops()
    {
        using var interrupts = new InterruptHandler(confirm: true);
        interrupts.OnCtrlC();
        Assert.True(interrupts.ConfirmRequested.IsCompleted);
        Assert.False(interrupts.StopRequested);

        // "No": the run goes on, and the next Ctrl+C asks again.
        interrupts.BeginConfirm();
        interrupts.EndConfirm(stop: false);
        Assert.False(interrupts.ConfirmRequested.IsCompleted);
        Assert.False(interrupts.StopRequested);

        // A Ctrl+C while the question is open stops at once.
        interrupts.OnCtrlC();
        interrupts.BeginConfirm();
        interrupts.OnCtrlC();
        Assert.True(interrupts.StopRequested);
    }

    [Fact]
    public void CtrlC_WithoutConfirmation_Stops()
    {
        using var interrupts = new InterruptHandler(confirm: false);
        interrupts.OnCtrlC();
        Assert.True(interrupts.StopRequested);
        Assert.False(interrupts.ConfirmRequested.IsCompleted);
    }

    [Fact]
    public void SleepGuard_IsAcquiredAndReleased()
    {
        using (SleepGuard guard = SleepGuard.Acquire())
        {
            Assert.False(string.IsNullOrEmpty(guard.Status));
            if (OperatingSystem.IsWindows())
            {
                Assert.True(guard.Active, guard.Status);
            }
        }
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
