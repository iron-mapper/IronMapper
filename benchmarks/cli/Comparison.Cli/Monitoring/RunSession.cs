using Comparison.Cli.Processes;
using Comparison.Cli.Ui;
using Spectre.Console;

namespace Comparison.Cli.Monitoring;

/// <summary>
/// Runs a script under a monitor: a live view in a terminal (Ctrl+C asks before stopping), or the script's lines plus
/// throttled progress lines in plain mode (Ctrl+C stops). Optionally keeps the machine awake for the duration.
/// </summary>
internal static class RunSession
{
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PlainRefresh = TimeSpan.FromSeconds(1);

    public static async Task<ScriptResult> RunAsync(ScriptCommand command, IRunMonitor monitor, bool keepAwake)
    {
        using SleepGuard? guard = keepAwake ? SleepGuard.Acquire() : null;
        if (guard is not null)
        {
            AnsiConsole.MarkupLine($"[grey]Sleep: {Markup.Escape(guard.Status)}[/]");
        }

        bool plain = Terminal.Plain;
        using var interrupts = new InterruptHandler(confirm: Terminal.Interactive);
        var stderr = new List<string>();
        using ScriptProcess process = ScriptProcess.Start(command,
            line =>
            {
                monitor.OnStdout(line);
                if (plain)
                {
                    Console.WriteLine(line);
                }
            },
            line =>
            {
                lock (stderr)
                {
                    stderr.Add(line);
                }

                monitor.OnStderr(line);
                if (plain)
                {
                    Console.Error.WriteLine(line);
                }
            });

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = interrupts.StopToken.Register(() => stopped.TrySetResult());

        bool interrupted = false;
        while (!process.Exited.IsCompleted)
        {
            Task trigger = Task.WhenAny(process.Exited, stopped.Task, interrupts.ConfirmRequested);
            if (plain)
            {
                await PlainLoopAsync(monitor, trigger).ConfigureAwait(false);
            }
            else
            {
                await LiveLoopAsync(monitor, trigger).ConfigureAwait(false);
            }

            if (process.Exited.IsCompleted)
            {
                break;
            }

            if (stopped.Task.IsCompleted)
            {
                await process.KillTreeAsync().ConfigureAwait(false);
                interrupted = true;
                break;
            }
            // Ctrl+C in a terminal: the live view has stopped; ask (a second Ctrl+C stops at once).
            interrupts.BeginConfirm();
            Task answer = ScriptRunner.AskToStopAsync(interrupts);
            await Task.WhenAny(answer, stopped.Task, process.Exited).ConfigureAwait(false);
            if (stopped.Task.IsCompleted && !process.Exited.IsCompleted)
            {
                await process.KillTreeAsync().ConfigureAwait(false);
                interrupted = true;
                break;
            }
        }

        int exitCode = interrupted ? ScriptRunner.InterruptedExitCode : await process.Exited.ConfigureAwait(false);
        monitor.Poll(DateTime.UtcNow);
        monitor.Complete(exitCode, interrupted);
        // The final view; plain output needs none (the script prints the end of every stage itself).
        if (!plain)
        {
            AnsiConsole.Write(monitor.Render(DateTime.UtcNow));
        }

        if (interrupted)
        {
            WriteStopped(monitor);
        }

        lock (stderr)
        {
            return new ScriptResult(exitCode, interrupted, stderr.ToList());
        }
    }

    private static async Task LiveLoopAsync(IRunMonitor monitor, Task trigger)
    {
        await AnsiConsole.Live(monitor.Render(DateTime.UtcNow))
            .AutoClear(true)
            .StartAsync(async context =>
            {
                while (!trigger.IsCompleted)
                {
                    monitor.Poll(DateTime.UtcNow);
                    context.UpdateTarget(monitor.Render(DateTime.UtcNow));
                    await Task.WhenAny(trigger, Task.Delay(LiveRefresh)).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }

    private static async Task PlainLoopAsync(IRunMonitor monitor, Task trigger)
    {
        while (!trigger.IsCompleted)
        {
            monitor.Poll(DateTime.UtcNow);
            foreach (string line in monitor.PlainUpdates(DateTime.UtcNow))
            {
                Console.WriteLine(line);
            }

            await Task.WhenAny(trigger, Task.Delay(PlainRefresh)).ConfigureAwait(false);
        }
    }

    private static void WriteStopped(IRunMonitor monitor)
    {
        AnsiConsole.MarkupLine("[yellow]Stopped: the script and every process it started (dotnet, BenchmarkDotNet) were killed.[/]");
        foreach (string path in monitor.PartialArtifacts)
        {
            AnsiConsole.MarkupLine($"  Partial results: {Markup.Escape(path)}");
        }

        AnsiConsole.MarkupLine("[grey]MSBuild build-server nodes started by dotnet build may stay in memory for a few minutes; " +
            "they end by themselves (or: dotnet build-server shutdown).[/]");
    }
}
