using Comparison.Cli.Parsing;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Comparison.Cli.Monitoring;

/// <summary>
/// Progress of run-all.ps1. From stdout: the mode, the results folder (results: ...), the steps and their estimates
/// (Expected duration ... (Build 1, Tests 1, ...)), the checks, each step's start (==> Step) and end (done in), and the
/// log of each dotnet command of a step (log: ...; a bdn-*.log is BenchmarkDotNet). The Startup step runs
/// run-startup.ps1, whose BenchmarkDotNet part writes raw/startup/bdn-startup.log after "Running StartupBenchmark".
/// Steps without a log show the scripts' last line.
/// </summary>
internal sealed class RunAllMonitor(string benchmarksDir) : IRunMonitor
{
    private static readonly TimeSpan PlainInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly List<Phase> _phases = [];
    private readonly List<PreflightCheck> _checks = [];
    private readonly Dictionary<string, int> _estimates = new(StringComparer.Ordinal);
    private Phase? _current;
    private string? _lastStdout;
    private string? _lastStderr;
    private DateTime _lastStdoutUtc = DateTime.UtcNow;
    private DateTime _lastPlainUtc = DateTime.MinValue;
    private string? _lastPlainText;
    private DateTime _lastHangWarningUtc = DateTime.MinValue;

    public string? Mode { get; private set; }

    /// <summary>The results folder as the script prints it (relative to benchmarks/, e.g. results/2.0.0).</summary>
    public string? ResultsDir { get; private set; }

    public string? ResultsFile { get; private set; }

    public RunAllPlan? Plan { get; private set; }

    public bool AllDone { get; private set; }

    public IReadOnlyList<PreflightCheck> Checks
    {
        get
        {
            lock (_gate)
            {
                return _checks.ToList();
            }
        }
    }

    public string? ResultsDirFullPath =>
        ResultsDir is null ? null : Path.GetFullPath(Path.IsPathRooted(ResultsDir) ? ResultsDir : Path.Combine(benchmarksDir, ResultsDir));

    public IReadOnlyList<string> PartialArtifacts
    {
        get
        {
            lock (_gate)
            {
                var paths = new List<string>();
                if (ResultsDirFullPath is { } results && Directory.Exists(Path.Combine(results, "raw")))
                {
                    paths.Add(Path.Combine(results, "raw"));
                }

                if (_current?.Tail is { Exists: true } tail)
                {
                    paths.Add(tail.Path);
                }

                return paths;
            }
        }
    }

    public void OnStdout(string line)
    {
        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;
            _lastStdoutUtc = now;
            switch (RunAllOutput.Parse(line))
            {
                case RunAllMode mode:
                    Mode = mode.Mode;
                    break;
                case RunAllResultsDir results:
                    ResultsDir = results.Dir;
                    break;
                case RunAllPlan plan:
                    Plan = plan;
                    foreach ((string step, int minutes) in plan.Steps)
                    {
                        _estimates[step] = minutes;
                        if (_phases.All(p => p.Name != step))
                        {
                            _phases.Add(new Phase(step) { Note = $"~{minutes} min" });
                        }
                    }
                    break;
                case RunAllPreflight check:
                    _checks.Add(check.Check);
                    break;
                case RunAllStepStarted started:
                    Phase phase = _phases.FirstOrDefault(p => p.Name == started.Step) ?? AddPhase(started.Step, started.EstimateMinutes);
                    phase.Start(now);
                    _current = phase;
                    break;
                case RunAllLog log when _current is not null:
                    _current.Follow(log.Path, now, Path.GetFileName(log.Path).StartsWith("bdn-", StringComparison.OrdinalIgnoreCase));
                    break;
                case RunAllStepDone when _current is not null:
                    _current.Poll();
                    _current.Finish(now, ok: true);
                    _current = null;
                    break;
                case RunAllDone done:
                    AllDone = true;
                    ResultsFile = done.ResultsFile;
                    break;
                case null when _current is { Name: "Startup" } && line.StartsWith("Running StartupBenchmark", StringComparison.Ordinal)
                                                              && ResultsDirFullPath is { } results:
                    _current.Follow(Path.Combine(results, "raw", "startup", "bdn-startup.log"), now, benchmarkDotNet: true);
                    break;
            }
            if (!string.IsNullOrWhiteSpace(line))
            {
                _lastStdout = line.Trim();
            }
        }
    }

    private Phase AddPhase(string step, int minutes)
    {
        var phase = new Phase(step) { Note = $"~{minutes} min" };
        _estimates[step] = minutes;
        _phases.Add(phase);
        return phase;
    }

    public void OnStderr(string line)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _lastStderr = line.Trim();
            }
        }
    }

    public void Poll(DateTime nowUtc)
    {
        lock (_gate)
        {
            _current?.Poll();
        }
    }

    /// <summary>After the script exited: a step still running failed (the script threw), unless the run was stopped.</summary>
    public void Complete(int exitCode, bool interrupted)
    {
        lock (_gate)
        {
            if (_current is not null && !interrupted)
            {
                _current.Finish(DateTime.UtcNow, ok: false, exitCode == 0 ? null : $"failed (exit code {exitCode})");
            }

            if (interrupted && _current is not null)
            {
                _current.Finish(DateTime.UtcNow, ok: false, "stopped");
            }

            _current = null;
        }
    }

    public IRenderable Render(DateTime nowUtc)
    {
        lock (_gate)
        {
            var rows = new List<IRenderable>
            {
                new Markup($"[bold]Full run[/] [grey]· {Markup.Escape(Mode ?? "?")} · results: {Markup.Escape(ResultsDir ?? "?")} · elapsed {Phase.Duration(nowUtc - _startedUtc)}" +
                           (Plan is null ? "" : $" of ~{Plan.TotalMinutes} min on the reference machine") + "[/]"),
            };
            if (_phases.Count > 0)
            {
                int detailWidth = Math.Max(20, AnsiConsole.Profile.Width - _phases.Max(p => p.Name.Length) - 16);
                var grid = new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap().RightAligned());
                foreach (Phase phase in _phases)
                {
                    grid.AddRow(new Markup(phase.Icon(nowUtc)), new Markup(Markup.Escape(phase.Name)), Detail(phase, detailWidth), new Markup($"[grey]{Phase.Duration(phase.Elapsed(nowUtc))}[/]"));
                }

                rows.Add(grid);
            }
            if (_phases.All(p => p.State == PhaseState.Pending))
            {
                int ok = _checks.Count(c => c.Mark == CheckMark.Ok);
                rows.Add(new Markup($"[yellow]{Markup.Escape(Spinner(nowUtc))}[/] Environment checks: {ok} ok" +
                    string.Concat(_checks.Where(c => c.Mark != CheckMark.Ok).Select(c => $"\n  {CheckView.Mark(c.Mark)} {Markup.Escape(c.Name)}: {Markup.Escape(c.Detail)}"))));
            }
            if (_current?.Tail is { } tail)
            {
                rows.Add(new Markup($"[grey]log: {Markup.Escape(tail.Path)}[/]"));
            }

            if (HangWarning(nowUtc) is { } warning)
            {
                rows.Add(new Markup($"[yellow]{Markup.Escape(warning)}[/]"));
            }

            if (_lastStderr is not null)
            {
                rows.Add(new Markup($"[red]{Markup.Escape(Shorten(_lastStderr, 160))}[/]"));
            }

            return new Rows(rows);
        }
    }

    private IRenderable Detail(Phase phase, int width)
    {
        switch (phase.State)
        {
            case PhaseState.Done:
                return new Markup("");
            case PhaseState.Failed:
                return new Markup($"[red]{Markup.Escape(Shorten(phase.Note ?? "failed", width))}[/]");
            case PhaseState.Running when phase.Bdn is { Recognized: true, Total: { } total } bdn:
                string text = bdn.CurrentBenchmark is null && phase.LastLine is { } last ? $"{bdn.Describe()} · {last}" : bdn.Describe();
                return new Markup($"{Phase.Bar(bdn.Completed, total)} {Markup.Escape(Shorten(text, width - 21))}");
            case PhaseState.Running:
                // A step without a BenchmarkDotNet log: the last line of its log, or of the script.
                string line = phase.LastLine ?? (_lastStdout is { } stdout && !stdout.StartsWith("==>", StringComparison.Ordinal) ? stdout : "running...");
                return new Markup($"[grey]{Markup.Escape(Shorten(line, width))}[/]");
            default:
                return new Markup($"[grey]{Markup.Escape(phase.Note ?? "")}[/]");
        }
    }

    public IReadOnlyList<string> PlainUpdates(DateTime nowUtc)
    {
        lock (_gate)
        {
            var lines = new List<string>();
            if (HangWarning(nowUtc) is { } warning && nowUtc - _lastHangWarningUtc >= Phase.HangThreshold)
            {
                _lastHangWarningUtc = nowUtc;
                lines.Add($"[{DateTime.Now:HH:mm:ss}] warning: {warning}");
            }
            // Steps with a log only: the scripts' own lines are already printed as they come.
            if (_current is { Tail: not null } phase && nowUtc - _lastPlainUtc >= PlainInterval)
            {
                string text = phase.Progress();
                if (text.Length > 0 && text != _lastPlainText)
                {
                    lines.Add($"[{DateTime.Now:HH:mm:ss}] {phase.Name}: {Shorten(text, 200)}");
                    _lastPlainText = text;
                }
                _lastPlainUtc = nowUtc;
            }
            return lines;
        }
    }

    private string? HangWarning(DateTime nowUtc)
    {
        // The step's log or the scripts' output, whichever moved last.
        DateTime last = _lastStdoutUtc;
        if (_current?.LastActivityUtc(_lastStdoutUtc) is { } activity && activity > last)
        {
            last = activity;
        }

        TimeSpan silent = nowUtc - last;
        if (_current is null || silent < Phase.HangThreshold)
        {
            return null;
        }

        string where = _current.Tail?.Path is { } path ? $"log: {path}" : $"last output: {_lastStdout}";
        return $"no output for {(int)silent.TotalMinutes} min in step {_current.Name} ({where}); the run is not stopped";
    }

    private static string Spinner(DateTime nowUtc)
    {
        IReadOnlyList<string> frames = Spectre.Console.Spinner.Known.Dots.Frames;
        return frames[(int)(nowUtc.Ticks / TimeSpan.FromMilliseconds(100).Ticks % frames.Count)];
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : max < 2 ? "" : text[..(max - 1)] + "…";
}
