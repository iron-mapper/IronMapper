using System.Diagnostics;
using Comparison.Cli.Commands;
using Comparison.Cli.Processes;

namespace Comparison.Cli.Tests;

/// <summary>The wrapper bench.ps1 and the ./bench.ps1 command the CLI prints for copying.</summary>
public class BenchCommandTests
{
    [Fact]
    public async Task UnquotedLists_ReachTheCli_AsOneArgument()
    {
        // pwsh hands a script an unquoted Nested,InPlace as an array. dotnet is replaced by a function that prints what
        // it gets (the build call returns at once), so the test needs no build of the CLI.
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string script = Path.Combine(dir.Path, "call.ps1");
        File.WriteAllText(script, $$"""
            function dotnet {
                if ($args[0] -eq 'build') { $global:LASTEXITCODE = 0; return }
                for ($i = 1; $i -lt $args.Count; $i++) { "[$($args[$i])] $($args[$i].GetType().Name)" }
                $global:LASTEXITCODE = 0
            }
            & '{{Path.Combine(Fixture.BenchmarksDir, "bench.ps1")}}' quick --filter Nested,InPlace --frameworks 'net10.0,net8.0' --branch x --dry
            """);

        (int exitCode, string[] stdout) = await RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-File", script], stdin: null);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["[quick] String", "[--filter] String", "[Nested,InPlace] String", "[--frameworks] String", "[net10.0,net8.0] String",
             "[--branch] String", "[x] String", "[--dry] String"],
            stdout);
    }

    [Theory]
    [InlineData("pwsh")]
    [InlineData("bash")]
    public async Task PrintedCommand_GivesTheSameArguments_InPwshAndBash(string shell)
    {
        string? executable = shell == "pwsh" ? Tools.FindOnPath("pwsh") : FindBash();
        if (executable is null)
            return;
        var quick = new QuickOptions { Benchmarks = ["Nested", "InPlace"], Frameworks = ["net10.0", "net8.0"], Branch = "it's", Dry = true };
        var full = new FullOptions { Mode = FullMode.Dry, ResultsDir = @"..\artifacts\bench dry (1)" };
        string[][] expected =
        [
            ["quick", "--filter", "Nested,InPlace", "--frameworks", "net10.0,net8.0", "--branch", "it's", "--dry"],
            ["full", "--mode", "dry", "--results-dir", @"..\artifacts\bench dry (1)"],
        ];

        // The printed line with the wrapper replaced by a function that prints its arguments one per line.
        string[] calls = [quick.ToBenchCommand(), full.ToBenchCommand()];
        Assert.All(calls, c => Assert.StartsWith(BenchCommand.Wrapper + " ", c));
        string lines = string.Join('\n', calls.Select(c => "show" + c[BenchCommand.Wrapper.Length..] + "\necho ---")) + "\n";
        // pwsh gets a file (with -Command - it echoes terminal control codes on Linux), bash its stdin (a Windows path
        // means nothing to WSL's bash).
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string script = Path.Combine(dir.Path, "show.ps1");
        File.WriteAllText(script, "function show { foreach ($a in $args) { \"[$a]\" } }\n" + lines);
        (int exitCode, string[] stdout) = shell == "pwsh"
            ? await RunAsync(executable, ["-NoProfile", "-NonInteractive", "-File", script], stdin: null)
            : await RunAsync(executable, ["-s"],"show() { for a in \"$@\"; do printf '[%s]\\n' \"$a\"; done; }\n" + lines);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected.SelectMany(e => e.Select(a => $"[{a}]").Append("---")), stdout);
    }

    /// <summary>
    /// bash from PATH; on Windows not the WSL launcher of System32 (or its WindowsApps alias), which needs an installed
    /// distribution — Git Bash, as on the GitHub runner.
    /// </summary>
    private static string? FindBash()
    {
        if (!OperatingSystem.IsWindows())
            return Tools.FindOnPath("bash");
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim('"'), "bash.exe");
            if (File.Exists(candidate)
                && !candidate.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)
                && !candidate.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    private static async Task<(int ExitCode, string[] Stdout)> RunAsync(string file, string[] arguments, string? stdin)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardInput = stdin is not null, UseShellExecute = false };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.ReplaceLineEndings("\n"));
            process.StandardInput.Close();
        }
        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray());
    }
}
