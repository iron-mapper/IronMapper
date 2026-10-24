using Comparison.Cli.Artifacts;
using Comparison.Cli.Parsing;
using Comparison.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Comparison.Cli.Monitoring;

/// <summary>
/// Progress of run-quick.ps1. From stdout: the run folder (Output:), the frameworks of the runs (Filter: ...;
/// frameworks: ...), the checks, and the stage lines ([HH:mm:ss] variant on framework: build|run|status). The current
/// stage's log is &lt;root&gt;/&lt;variant&gt;-&lt;framework&gt;/build.log or run.log (BenchmarkDotNet). The copy of summary.md
/// the script prints at the end is not shown live (plain output passes it on): the command shows the file afterwards.
/// </summary>
internal sealed class QuickMonitor : IRunMonitor
{
    private static readonly TimeSpan PlainInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly List<Phase> _phases = [];
    private readonly List<PreflightCheck> _checks = [];
    private Phase? _current;
    private bool _inSummaryEcho;
    private string? _lastStdout;
    private string? _lastStderr;
    private DateTime _lastStdoutUtc = DateTime.UtcNow;
    private DateTime _lastPlainUtc = DateTime.MinValue;
    private string? _lastPlainText;
    private DateTime _lastHangWarningUtc = DateTime.MinValue;

    public string? Root { get; private set; }

    public string? SummaryPath { get; private set; }

    public QuickHeader? Header { get; private set; }

    public string? RunsPlanned { get; private set; }

    public IReadOnlyList<PreflightCheck> Checks
    {
        get { lock (_gate) return _checks.ToList(); }
    }

    public IReadOnlyList<string> PartialArtifacts
    {
        get
        {
            lock (_gate)
            {
                var paths = new List<string>();
                if (Root is not null && Directory.Exists(Root))
                    paths.Add(Root);
                if (_current?.Tail is { Exists: true } tail)
                    paths.Add(tail.Path);
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
            QuickEvent? e = QuickOutput.Parse(line);
            switch (e)
            {
                case QuickOutputRoot root:
                    Root = root.Path;
                    break;
                case QuickHeader header:
                    Header = header;
                    CreatePhases(header.Frameworks);
                    break;
                case QuickPreflight check:
                    _checks.Add(check.Check);
                    break;
                case QuickRunsPlanned runs:
                    RunsPlanned = runs.Text;
                    break;
                case QuickStage stage:
                    OnStage(stage, now);
                    break;
                case QuickSummaryEcho:
                    _inSummaryEcho = true;
                    break;
                case QuickSummaryWritten summary:
                    _inSummaryEcho = false;
                    SummaryPath = summary.Path;
                    break;
            }
            if (!_inSummaryEcho && !string.IsNullOrWhiteSpace(line))
                _lastStdout = line.Trim();
        }
    }

    public void OnStderr(string line)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(line))
                _lastStderr = line.Trim();
        }
    }

    public void Poll(DateTime nowUtc)
    {
        lock (_gate)
            _current?.Poll();
    }

    private void CreatePhases(IReadOnlyList<string> frameworks)
    {
        if (_phases.Count > 0)
            return;
        foreach (string framework in frameworks)
        {
            foreach (string variant in QuickRun.Variants)
            {
                _phases.Add(new Phase(PhaseName(variant, framework, "build")));
                _phases.Add(new Phase(PhaseName(variant, framework, "run")));
            }
        }
    }

    private void OnStage(QuickStage stage, DateTime now)
    {
        string kind = stage.Kind == QuickStageKind.Build ? "build" : "run";
        Phase phase = FindOrAdd(PhaseName(stage.Variant, stage.Framework, kind));
        string runDir = Root is null ? "" : Path.Combine(Root, $"{stage.Variant}-{stage.Framework}");
        switch (stage.Kind)
        {
            case QuickStageKind.Build:
                phase.Start(now);
                if (Root is not null)
                    phase.Follow(Path.Combine(runDir, "build.log"), now, benchmarkDotNet: false);
                _current = phase;
                break;
            case QuickStageKind.Run:
                Phase build = FindOrAdd(PhaseName(stage.Variant, stage.Framework, "build"));
                if (build.IsActive)
                {
                    build.Poll();
                    build.Finish(now, ok: true);
                }
                phase.Start(now);
                if (Root is not null)
                    phase.Follow(Path.Combine(runDir, "run.log"), now, benchmarkDotNet: true);
                _current = phase;
                break;
            default:
                phase.Poll();
                phase.Finish(now, RunStatus.Classify(stage.Text) == RunState.Ok, stage.Text);
                if (_current == phase)
                    _current = null;
                break;
        }
    }

    private Phase FindOrAdd(string name)
    {
        Phase? phase = _phases.FirstOrDefault(p => p.Name == name);
        if (phase is null)
        {
            phase = new Phase(name);
            _phases.Add(phase);
        }
        return phase;
    }

    private static string PhaseName(string variant, string framework, string kind) => $"{variant} {framework} {kind}";

    public IRenderable Render(DateTime nowUtc)
    {
        lock (_gate)
        {
            var rows = new List<IRenderable>();
            string job = Header?.Job ?? "";
            rows.Add(new Markup($"[bold]Quick A/B[/] [grey]· {Markup.Escape(job)} · elapsed {Phase.Duration(nowUtc - _startedUtc)}[/]"));

            if (_phases.Count > 0)
            {
                var grid = new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn(new GridColumn().NoWrap().RightAligned());
                // The detail column gets what the terminal has left: icon, name, time and the grid padding are fixed.
                int detailWidth = Math.Max(20, AnsiConsole.Profile.Width - _phases.Max(p => p.Name.Length) - 16);
                foreach (Phase phase in _phases)
                    grid.AddRow(new Markup(phase.Icon(nowUtc)), new Markup(Markup.Escape(phase.Name)), Detail(phase, detailWidth), new Markup($"[grey]{Phase.Duration(phase.Elapsed(nowUtc))}[/]"));
                rows.Add(grid);
            }
            if (_phases.All(p => p.State == PhaseState.Pending) && RunsPlanned is null)
            {
                // The checks run first (restore and the nuget.org comparison take a few seconds).
                int ok = _checks.Count(c => c.Mark == CheckMark.Ok);
                rows.Add(new Markup($"[yellow]{Markup.Escape(SpinnerText(nowUtc))}[/] Pre-flight checks: {ok} ok" +
                    string.Concat(_checks.Where(c => c.Mark != CheckMark.Ok).Select(c => $"\n  {CheckView.Mark(c.Mark)} {Markup.Escape(c.Name)}: {Markup.Escape(c.Detail)}"))));
            }

            if (_current?.Tail is { } tail)
                rows.Add(new Markup($"[grey]log: {Markup.Escape(tail.Path)}[/]"));
            if (HangWarning(nowUtc) is { } warning)
                rows.Add(new Markup($"[yellow]{Markup.Escape(warning)}[/]"));
            if (_lastStderr is not null)
                rows.Add(new Markup($"[red]{Markup.Escape(Shorten(_lastStderr, 160))}[/]"));
            if (Header is not null && _current is null && _phases.Count > 0 && _phases.All(p => p.State is PhaseState.Done or PhaseState.Failed))
                rows.Add(new Markup("[grey]Writing summary.md...[/]"));
            return new Rows(rows);
        }
    }

    private static IRenderable Detail(Phase phase, int width)
    {
        switch (phase.State)
        {
            case PhaseState.Done:
            case PhaseState.Failed:
                string color = phase.State == PhaseState.Done ? "green" : "red";
                return new Markup(phase.Note is null ? "" : $"[{color}]{Markup.Escape(Shorten(phase.Note, width))}[/]");
            case PhaseState.Running when phase.Bdn is { Recognized: true, Total: { } total } bdn:
                // Before the first case BenchmarkDotNet builds the benchmark project: show what it prints.
                string text = bdn.CurrentBenchmark is null && phase.LastLine is { } last ? $"{bdn.Describe()} · {last}" : bdn.Describe();
                return new Markup($"{Phase.Bar(bdn.Completed, total)} {Markup.Escape(Shorten(text, width - 21))}");
            case PhaseState.Running:
                return new Markup($"[grey]{Markup.Escape(Shorten(phase.LastLine ?? "starting...", width))}[/]");
            default:
                return new Markup("");
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
            if (_current is { } phase && nowUtc - _lastPlainUtc >= PlainInterval)
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

    /// <summary>"no log output for N min (log: ...)" when the current phase has been silent too long.</summary>
    private string? HangWarning(DateTime nowUtc)
    {
        DateTime last = _current?.LastActivityUtc(_lastStdoutUtc) ?? _lastStdoutUtc;
        if (_current is null && _lastStdoutUtc > last)
            last = _lastStdoutUtc;
        TimeSpan silent = nowUtc - last;
        if (silent < Phase.HangThreshold)
            return null;
        string where = _current?.Tail?.Path is { } path ? $"log: {path}" : $"last output: {_lastStdout}";
        return $"no log output for {(int)silent.TotalMinutes} min ({where}); the run is not stopped";
    }

    private static string SpinnerText(DateTime nowUtc)
    {
        IReadOnlyList<string> frames = Spinner.Known.Dots.Frames;
        return frames[(int)(nowUtc.Ticks / TimeSpan.FromMilliseconds(100).Ticks % frames.Count)];
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : max < 2 ? "" : text[..(max - 1)] + "…";
}
