using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Comparison.Cli.Processes;

/// <summary>
/// Keeps the machine awake while a run is in progress (the scripts run-quick.ps1 and run-benchmarks.ps1 do it on their
/// own as well; run-all.ps1 does not). Windows: SetThreadExecutionState on a dedicated thread that lives as long as the
/// guard (the flag belongs to the thread, and async continuations move between pool threads). macOS: caffeinate -i -w
/// &lt;CLI pid&gt;. Linux: systemd-inhibit, when present. Dispose releases it, also after an error or Ctrl+C.
/// </summary>
internal sealed class SleepGuard : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private readonly ManualResetEventSlim? _release;
    private readonly Thread? _thread;
    private readonly Process? _helper;

    private SleepGuard(string status, ManualResetEventSlim? release = null, Thread? thread = null, Process? helper = null)
    {
        Status = status;
        _release = release;
        _thread = thread;
        _helper = helper;
    }

    /// <summary>What the guard does, or why it could not, for the run header (a warning, not an error).</summary>
    public string Status { get; }

    public bool Active => _thread is not null || _helper is not null;

    public static SleepGuard Acquire()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return AcquireWindows();
            int pid = Environment.ProcessId;
            if (OperatingSystem.IsMacOS())
                return StartHelper("caffeinate", ["-i", "-w", pid.ToString()], "caffeinate -i");
            if (OperatingSystem.IsLinux())
            {
                // tail --pid ends with the CLI, so the inhibitor cannot outlive a crashed CLI.
                return StartHelper("systemd-inhibit",
                    ["--what=sleep:idle", "--who=IronMapper benchmarks", "--why=Benchmark run", "--mode=block",
                     "tail", $"--pid={pid}", "-f", "/dev/null"],
                    "systemd-inhibit");
            }
        }
        catch (Exception ex)
        {
            return new SleepGuard($"sleep prevention failed: {ex.Message}");
        }
        return new SleepGuard("sleep prevention not supported on this OS");
    }

    private static SleepGuard AcquireWindows()
    {
        var release = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        uint previous = 0;
        var thread = new Thread(() =>
        {
            previous = SetThreadExecutionState(EsContinuous | EsSystemRequired);
            started.Set();
            release.Wait();
            SetThreadExecutionState(EsContinuous);
        })
        {
            IsBackground = true,
            Name = "SleepGuard",
        };
        thread.Start();
        started.Wait();
        return previous == 0
            ? new SleepGuard("sleep prevention failed (SetThreadExecutionState)", release, thread)
            : new SleepGuard("system sleep blocked (SetThreadExecutionState)", release, thread);
    }

    private static SleepGuard StartHelper(string tool, string[] arguments, string description)
    {
        string? path = Tools.FindOnPath(tool);
        if (path is null)
            return new SleepGuard($"{tool} not found: the machine may sleep during the run");
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            info.ArgumentList.Add(arg);
        Process helper = Process.Start(info)!;
        return new SleepGuard($"system sleep blocked ({description})", helper: helper);
    }

    public void Dispose()
    {
        if (_release is not null)
        {
            _release.Set();
            _thread?.Join(TimeSpan.FromSeconds(5));
            _release.Dispose();
        }
        if (_helper is not null)
        {
            try
            {
                if (!_helper.HasExited)
                    _helper.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            _helper.Dispose();
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
