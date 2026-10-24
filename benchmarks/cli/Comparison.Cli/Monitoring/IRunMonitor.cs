using Spectre.Console.Rendering;

namespace Comparison.Cli.Monitoring;

/// <summary>
/// Turns a script's output and logs into progress. Output callbacks come from the process's reader threads, the other
/// members from the display loop; implementations synchronize themselves.
/// </summary>
internal interface IRunMonitor
{
    void OnStdout(string line);

    void OnStderr(string line);

    /// <summary>Reads what the logs of the current phase got since the last call.</summary>
    void Poll(DateTime nowUtc);

    /// <summary>The live view (terminal).</summary>
    IRenderable Render(DateTime nowUtc);

    /// <summary>Progress lines for plain output, throttled by the monitor (the script's own lines are printed as they come).</summary>
    IReadOnlyList<string> PlainUpdates(DateTime nowUtc);

    /// <summary>The script has exited (or was stopped); called before the final view.</summary>
    void Complete(int exitCode, bool interrupted)
    {
    }

    /// <summary>Where a stopped run left its files, for the message after Ctrl+C.</summary>
    IReadOnlyList<string> PartialArtifacts { get; }
}
