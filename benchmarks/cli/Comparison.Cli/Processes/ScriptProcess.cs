using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Comparison.Cli.Processes;

/// <summary>
/// A running script: pwsh with stdout and stderr read line by line. The process gets no console of ours
/// (CreateNoWindow), so Ctrl+C in the terminal reaches only the CLI, which then decides whether to stop the run and
/// kills the whole tree (pwsh, dotnet, the BenchmarkDotNet processes). On Linux it is started through setsid when
/// available, which puts it in its own session for the same reason; on macOS the terminal's Ctrl+C reaches it directly.
/// <para>
/// Windows: the hidden console of a CreateNoWindow process starts with the OEM code page (866 on a Russian system).
/// pwsh decodes the output of native commands with its console's code page, and dotnet/MSBuild write UTF-8, so the
/// scripts' <c>dotnet ... *&gt; $log</c> would write mojibake into build.log and run.log. The script therefore runs as
/// <c>cmd /c "chcp 65001 &gt;nul &amp;&amp; pwsh ... -File ..."</c>: the hidden console is UTF-8 before pwsh starts, as in a
/// UTF-8 terminal, and pwsh's own output is UTF-8 too.
/// </para>
/// </summary>
internal sealed partial class ScriptProcess : IDisposable
{
    private readonly Process _process;

    private ScriptProcess(Process process, Task<int> exited)
    {
        _process = process;
        Exited = exited;
    }

    public int Id => _process.Id;

    /// <summary>Completes with the exit code once the process has exited and both streams are read to the end.</summary>
    public Task<int> Exited { get; }

    /// <summary>The encoding of the script's redirected output: UTF-8 everywhere (on Windows through chcp 65001).</summary>
    public static Encoding OutputEncoding { get; } = new UTF8Encoding(false);

    public static ScriptProcess Start(ScriptCommand command, Action<string> onStdout, Action<string> onStderr)
    {
        ProcessStartInfo info = CreateStartInfo(command);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { onStdout(StripAnsi(e.Data)); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { onStderr(StripAnsi(e.Data)); } };
        process.Start();
        // -NonInteractive already refuses prompts; a closed stdin makes sure nothing waits for input.
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new ScriptProcess(process, WaitAsync(process));
    }

    internal static ProcessStartInfo CreateStartInfo(ScriptCommand command)
    {
        string pwsh = Tools.FindOnPath("pwsh") ?? "pwsh";
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            string cmd = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec
                ? comSpec
                : Path.Combine(Environment.SystemDirectory, "cmd.exe");
            // /d: no AutoRun commands; /v:off: no !delayed! expansion; /s /c "...": the outer quotes are removed and the
            // rest runs as it is. The exit code of `a && b` is b's, the script's.
            info = new ProcessStartInfo(cmd)
            {
                Arguments = $"/d /v:off /s /c \"chcp 65001 >nul && {CmdQuote(pwsh)} {string.Join(' ', command.PwshArguments.Select(CmdQuote))}\"",
            };
        }
        else
        {
            // setsid execs pwsh in place (the child is not a process group leader), so the PID stays pwsh's.
            string? setsid = OperatingSystem.IsLinux() ? Tools.FindOnPath("setsid") : null;
            info = new ProcessStartInfo(setsid ?? pwsh);
            if (setsid is not null)
            {
                info.ArgumentList.Add(pwsh);
            }

            foreach (string arg in command.PwshArguments)
            {
                info.ArgumentList.Add(arg);
            }
        }
        info.WorkingDirectory = command.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = OutputEncoding;
        info.StandardErrorEncoding = OutputEncoding;
        return info;
    }

    /// <summary>
    /// One argument for the cmd /c line: in double quotes, where cmd leaves &amp; | &lt; &gt; ( ) ^ * , and spaces alone and pwsh
    /// takes the content literally. Trailing backslashes are doubled so they do not escape the closing quote. A double
    /// quote or a percent sign (cmd would expand %VAR% even in quotes) is refused: no script parameter needs one.
    /// </summary>
    internal static string CmdQuote(string arg)
    {
        if (arg.IndexOfAny(['"', '%', '\r', '\n']) >= 0)
        {
            throw new ArgumentException($"An argument with \", % or a line break cannot be passed to the script: {arg}");
        }

        int trailing = arg.Length - arg.TrimEnd('\\').Length;
        return "\"" + arg + new string('\\', trailing) + "\"";
    }

    private static async Task<int> WaitAsync(Process process)
    {
        // WaitForExitAsync also waits for the end of the redirected streams.
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Kills cmd, pwsh and every process they started (dotnet, BenchmarkDotNet), then waits for the exit.</summary>
    public async Task KillTreeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited in the meantime.
        }
        try
        {
            await Exited.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    public void Dispose() => _process.Dispose();

    /// <summary>
    /// Removes ANSI escape sequences: pwsh colours its error view (and $PSStyle output) even with redirected streams
    /// when its hidden console supports VT. The script's environment is left alone; the CLI reads plain text.
    /// </summary>
    internal static string StripAnsi(string line) => line.Contains('\u001b') ? AnsiSequence().Replace(line, "") : line;

    [GeneratedRegex(@"\u001b(?:\[[0-?]*[ -/]*[@-~]|\][^\u0007\u001b]*(?:\u0007|\u001b\\)|[@-Z\\-_])")]
    private static partial Regex AnsiSequence();
}
