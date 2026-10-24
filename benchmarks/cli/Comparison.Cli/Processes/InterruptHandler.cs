namespace Comparison.Cli.Processes;

/// <summary>
/// Ctrl+C while a script runs. With confirmation (an interactive terminal) the first Ctrl+C only raises
/// <see cref="ConfirmRequested"/>; a Ctrl+C while the question is open (<see cref="BeginConfirm"/>) stops at once.
/// Without confirmation (no terminal to ask in) the first Ctrl+C stops the run. "Stop" cancels <see cref="StopToken"/>.
/// </summary>
internal sealed class InterruptHandler : IDisposable
{
    private readonly bool _confirm;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private TaskCompletionSource _confirmRequested = NewSignal();
    private bool _confirming;

    public InterruptHandler(bool confirm)
    {
        _confirm = confirm;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <summary>Cancelled when the run must be stopped.</summary>
    public CancellationToken StopToken => _stop.Token;

    public bool StopRequested => _stop.IsCancellationRequested;

    /// <summary>Completes when the user pressed Ctrl+C and should be asked whether to stop.</summary>
    public Task ConfirmRequested
    {
        get
        {
            lock (_gate)
            {
                return _confirmRequested.Task;
            }
        }
    }

    /// <summary>Marks the question as open: another Ctrl+C now stops the run.</summary>
    public void BeginConfirm()
    {
        lock (_gate)
        {
            _confirming = true;
        }
    }

    /// <summary>The user answered: stop, or go on (a later Ctrl+C asks again).</summary>
    public void EndConfirm(bool stop)
    {
        lock (_gate)
        {
            _confirming = false;
            _confirmRequested = NewSignal();
        }
        if (stop)
        {
            RequestStop();
        }
    }

    public void RequestStop()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // The CLI decides; the script never sees the Ctrl+C (ScriptProcess starts it without our console).
        e.Cancel = true;
        OnCtrlC();
    }

    /// <summary>One Ctrl+C (internal for tests).</summary>
    internal void OnCtrlC()
    {
        lock (_gate)
        {
            if (!_confirm || _confirming)
            {
                RequestStop();
                return;
            }
            _confirmRequested.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _stop.Dispose();
    }
}
