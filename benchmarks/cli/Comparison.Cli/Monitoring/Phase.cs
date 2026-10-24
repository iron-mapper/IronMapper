using Comparison.Cli.Parsing;
using Spectre.Console;

namespace Comparison.Cli.Monitoring;

internal enum PhaseState
{
    Pending,
    Running,
    Done,
    Failed,
}

/// <summary>
/// One stage of a run (build or run of a variant × framework; a step of run-all.ps1): its state, times, and the log it
/// writes, followed while it runs. A BenchmarkDotNet log also feeds <see cref="Bdn"/>; any log gives the last line.
/// </summary>
internal sealed class Phase(string name)
{
    /// <summary>
    /// No growth of the current log (or of stdout, when the phase has no log) for this long shows a warning; the run
    /// goes on. The longest silences seen in normal runs are about 30 s: BenchmarkDotNet's own build of the benchmark
    /// project prints nothing until it is done, and so do the restore and the build of a variant. While benchmarks run,
    /// BenchmarkDotNet prints a line per iteration (an iteration is ~0.5 s by default; one Collection 100 000
    /// operation, the slowest case, ~0.3 s). Five minutes is ten times the longest normal silence, so a normal run
    /// never sees the warning, and a hang still shows up long before anyone would look at the clock.
    /// </summary>
    public static readonly TimeSpan HangThreshold = TimeSpan.FromMinutes(5);

    public string Name { get; } = name;

    public PhaseState State { get; private set; }

    public DateTime? StartedUtc { get; private set; }

    public DateTime? EndedUtc { get; private set; }

    /// <summary>Result text (a run status), an estimate, or why it failed.</summary>
    public string? Note { get; set; }

    public LogTail? Tail { get; private set; }

    public BdnProgress? Bdn { get; private set; }

    public string? LastLine { get; private set; }

    public bool IsActive => State == PhaseState.Running;

    public void Start(DateTime nowUtc)
    {
        State = PhaseState.Running;
        StartedUtc = nowUtc;
    }

    /// <summary>Follows a log from now on (a phase may write several, one after the other).</summary>
    public void Follow(string path, DateTime nowUtc, bool benchmarkDotNet)
    {
        // A little slack for the clocks of file times; the log of an earlier run is hours old.
        Tail = new LogTail(path, nowUtc.AddSeconds(-5));
        Bdn = benchmarkDotNet ? new BdnProgress() : null;
        LastLine = null;
    }

    public void Finish(DateTime nowUtc, bool ok, string? note = null)
    {
        State = ok ? PhaseState.Done : PhaseState.Failed;
        EndedUtc = nowUtc;
        if (note is not null)
            Note = note;
    }

    public void Poll()
    {
        if (Tail is null)
            return;
        foreach (string line in Tail.Poll())
        {
            Bdn?.Feed(line);
            if (!string.IsNullOrWhiteSpace(line))
                LastLine = line.Trim();
        }
    }

    public TimeSpan? Elapsed(DateTime nowUtc) => StartedUtc is { } start ? (EndedUtc ?? nowUtc) - start : null;

    /// <summary>When the phase last showed signs of life: its start, or the last growth of its log.</summary>
    public DateTime LastActivityUtc(DateTime fallbackUtc)
    {
        DateTime last = StartedUtc ?? fallbackUtc;
        if (Tail?.LastGrowthUtc is { } growth && growth > last)
            last = growth;
        return last;
    }

    /// <summary>Progress text: BenchmarkDotNet's when it is recognized, otherwise the last log line.</summary>
    public string Progress()
    {
        if (Bdn is { Recognized: true })
            return Bdn.Describe();
        return LastLine ?? "";
    }

    public static string Duration(TimeSpan? value) => value is { } t ? t.ToString(@"h\:mm\:ss") : "";

    public static string Bar(int done, int total, int width = 20)
    {
        int filled = total > 0 ? Math.Clamp((int)Math.Round(width * (double)done / total), 0, width) : 0;
        return new string('█', filled) + new string('░', width - filled);
    }

    /// <summary>The icon of a state; a running phase spins.</summary>
    public string Icon(DateTime nowUtc) => State switch
    {
        PhaseState.Done => "[green]✓[/]",
        PhaseState.Failed => "[red]✗[/]",
        PhaseState.Running => $"[yellow]{Markup.Escape(SpinnerFrame(nowUtc))}[/]",
        _ => "[grey]·[/]",
    };

    private static string SpinnerFrame(DateTime nowUtc)
    {
        IReadOnlyList<string> frames = Spinner.Known.Dots.Frames;
        return frames[(int)(nowUtc.Ticks / TimeSpan.FromMilliseconds(100).Ticks % frames.Count)];
    }
}
