using Comparison.Cli.Parsing;
using Comparison.Cli.Ui;
using Spectre.Console;

namespace Comparison.Cli.Processes;

/// <summary>The outcome of a script run.</summary>
/// <param name="ExitCode">The script's exit code, or <see cref="ScriptRunner.InterruptedExitCode"/> after Ctrl+C.</param>
internal sealed record ScriptResult(int ExitCode, bool Interrupted, IReadOnlyList<string> Stderr)
{
    public string ErrorMessage => ScriptError.Extract(Stderr);
}

/// <summary>Starts a script, passes its output lines on, and stops it (the whole process tree) on request.</summary>
internal static class ScriptRunner
{
    /// <summary>Exit code of the CLI after the user stopped a run (128 + SIGINT, as shells report it).</summary>
    public const int InterruptedExitCode = 130;

    /// <summary>Prints the command to copy: the exact pwsh invocation and its working directory.</summary>
    public static void PrintCommand(ScriptCommand command, BenchPaths paths)
    {
        AnsiConsole.MarkupLine($"[grey]Equivalent command, in benchmarks/ ({Markup.Escape(paths.BenchmarksDir)}):[/]");
        // Written directly, never wrapped: it must stay one line to copy.
        Console.WriteLine("  " + command.Display);
    }

    /// <summary>
    /// Runs the script to the end, or until <paramref name="interrupts"/> asks to stop; then kills the process tree.
    /// <paramref name="onConfirm"/> is called when the user pressed Ctrl+C and must be asked (interactive runs); it
    /// answers through <see cref="InterruptHandler.EndConfirm"/>.
    /// </summary>
    public static async Task<ScriptResult> RunAsync(
        ScriptCommand command,
        Action<string> onStdout,
        Action<string> onStderr,
        InterruptHandler interrupts,
        Func<Task>? onConfirm = null)
    {
        var stderr = new List<string>();
        using ScriptProcess process = ScriptProcess.Start(command, onStdout, line =>
        {
            lock (stderr)
            {
                stderr.Add(line);
            }

            onStderr(line);
        });

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = interrupts.StopToken.Register(() => stopped.TrySetResult());
        while (true)
        {
            Task winner = await Task.WhenAny(process.Exited, stopped.Task, interrupts.ConfirmRequested).ConfigureAwait(false);
            if (winner == process.Exited)
            {
                break;
            }

            if (winner == stopped.Task)
            {
                await process.KillTreeAsync().ConfigureAwait(false);
                lock (stderr)
                {
                    return new ScriptResult(InterruptedExitCode, true, stderr.ToList());
                }
            }
            if (onConfirm is null)
            {
                interrupts.EndConfirm(stop: true);
                continue;
            }
            interrupts.BeginConfirm();
            Task answer = onConfirm();
            await Task.WhenAny(answer, stopped.Task, process.Exited).ConfigureAwait(false);
        }

        int exitCode = await process.Exited.ConfigureAwait(false);
        lock (stderr)
        {
            return new ScriptResult(exitCode, false, stderr.ToList());
        }
    }

    /// <summary>Asks on the console whether to stop the run (y/N); a second Ctrl+C meanwhile stops at once.</summary>
    public static Task AskToStopAsync(InterruptHandler interrupts) => Task.Run(() =>
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[yellow]Stop the run? Partial results stay on disk. [[y/N]][/] ");
        bool stop = false;
        try
        {
            // Only y, n, Enter and Escape answer; anything else (a stray key event) is ignored.
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key is ConsoleKey.Y or ConsoleKey.N or ConsoleKey.Enter or ConsoleKey.Escape)
                {
                    stop = key.Key == ConsoleKey.Y;
                    break;
                }
            }
            AnsiConsole.WriteLine(stop ? "y" : "n");
        }
        catch (InvalidOperationException)
        {
            stop = true;
        }
        interrupts.EndConfirm(stop);
    });

    /// <summary>Writes a stderr line of the script (dimmed red, never markup).</summary>
    public static void WriteStderr(string line)
    {
        if (Terminal.Plain)
        {
            Console.Error.WriteLine(line);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(line)}[/]");
        }
    }
}
