using System.Collections.ObjectModel;
using AppStore.Core.Models;
using AppStore.Core.Winget;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStore.Core.Services;

/// <summary>One install / update / uninstall shown on the Queue page.</summary>
public sealed partial class OperationItem : ObservableObject
{
    public OperationItem(OperationRequest request) => Request = request;

    public OperationRequest Request { get; }

    public string Id => Request.Id;

    public string Name => Request.Name;

    public string TargetText => Request.Kind switch
    {
        OpKind.Uninstall => "uninstall",
        _ => Request.Version.Length > 0 ? "→ " + Request.Version : "latest",
    };

    [ObservableProperty]
    public partial OpState State { get; set; }

    /// <summary>0–100, or -1 while the step has no measurable progress.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; } = -1;

    [ObservableProperty]
    public partial string Phase { get; set; } = "Waiting";

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    public string Log { get; set; } = string.Empty;

    public bool IsRunning => State == OpState.Running;

    public bool IsIndeterminate => Percent < 0;

    public bool IsFailed => State == OpState.Failed;

    public bool IsDone => State == OpState.Done;

    internal CancellationTokenSource? Cts { get; set; }

    internal bool CancelRequested { get; set; }

    partial void OnStateChanged(OpState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsDone));
    }

    partial void OnPercentChanged(double value) => OnPropertyChanged(nameof(IsIndeterminate));
}

/// <summary>Runs one request: closes the app first if asked, runs winget, retries once if asked.</summary>
public static class BatchRunner
{
    public static async Task<OpResult> RunOneAsync(IPackageManager pm, OperationRequest req, int index, Action<BatchEvent> onEvent, CancellationToken ct)
    {
        onEvent(new BatchEvent { Index = index, State = OpState.Running, Phase = "Starting…" });
        try
        {
            if (req.CloseAppFirst && req.Kind != OpKind.Install && AppCloser.IsRunning(req.Name))
            {
                onEvent(new BatchEvent { Index = index, State = OpState.Running, Phase = "Closing " + req.Name + "…" });
                if (!await AppCloser.CloseAsync(req.Name, ct).ConfigureAwait(false))
                {
                    var skip = OpResult.Skip(req.Name + " couldn't be closed, so it wasn't changed. Close it and try again.");
                    onEvent(new BatchEvent { Index = index, State = OpState.Skipped, Message = skip.Message });
                    return skip;
                }
            }

            var result = await pm.RunAsync(req, new SyncProgress(p => onEvent(new BatchEvent { Index = index, State = OpState.Running, Percent = p.Percent, Phase = p.Phase })), ct).ConfigureAwait(false);
            if (!result.Success && req.RetryOnce && !ct.IsCancellationRequested && IsWorthRetrying(result.ExitCode))
            {
                onEvent(new BatchEvent { Index = index, State = OpState.Running, Percent = -1, Phase = "Trying again…" });
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                result = await pm.RunAsync(req, new SyncProgress(p => onEvent(new BatchEvent { Index = index, State = OpState.Running, Percent = p.Percent, Phase = p.Phase })), ct).ConfigureAwait(false);
            }

            onEvent(new BatchEvent
            {
                Index = index,
                State = result.Success ? OpState.Done : OpState.Failed,
                Percent = 100,
                Message = result.Success ? result.Message : result.Message + (result.Log.Length > 0 ? "\n" + result.Log : string.Empty),
            });
            return result;
        }
        catch (OperationCanceledException)
        {
            onEvent(new BatchEvent { Index = index, State = OpState.Cancelled, Message = "Cancelled" });
            return OpResult.Fail("Cancelled");
        }
    }

    /// <summary>Errors where a second try can't help (cancelled, nothing to update, blocked, needs reboot…).</summary>
    internal static bool IsWorthRetrying(int exitCode) => unchecked((uint)exitCode) switch
    {
        0x8A15002B or 0x8A15010A or 0x8A15010B or 0x8A15010C or 0x8A150103 or 0x8A150109 or 0x8A150104 or 0x800704C7 or 0x8A150011 => false,
        _ => true,
    };

    /// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; would post to a captured context).</summary>
    private sealed class SyncProgress(Action<OpProgress> report) : IProgress<OpProgress>
    {
        public void Report(OpProgress value) => report(value);
    }
}

/// <summary>The app's operations queue: batches run one after another; each finished item lands in History.</summary>
public sealed partial class OperationQueue : ObservableObject
{
    private readonly IPackageManager _pm;
    private readonly HistoryStore _history;
    private readonly IElevation? _elevation;
    private readonly Queue<(List<OperationItem> Items, bool Elevate)> _pending = new();
    private bool _running;
    private List<OperationItem>? _elevatedBatch;

    public OperationQueue(IPackageManager pm, HistoryStore history, IElevation? elevation)
    {
        _pm = pm;
        _history = history;
        _elevation = elevation;
    }

    public ObservableCollection<OperationItem> Active { get; } = [];

    public ObservableCollection<OperationItem> Finished { get; } = [];

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>True while the current batch runs in the elevated helper (one admin popup).</summary>
    [ObservableProperty]
    public partial bool ElevatedBatchRunning { get; set; }

    public int LeftCount => Active.Count;

    /// <summary>Raised on the UI thread when an item finishes (lists refresh from this).</summary>
    public event EventHandler<OperationItem>? ItemFinished;

    public event EventHandler? QueueEmptied;

    public bool Contains(string id) => Active.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void Enqueue(IReadOnlyList<OperationRequest> requests, bool elevate)
    {
        var items = requests.Where(r => !Contains(r.Id)).Select(r => new OperationItem(r)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        foreach (var i in items)
        {
            Active.Add(i);
        }

        OnPropertyChanged(nameof(LeftCount));
        _pending.Enqueue((items, elevate && _elevation is { IsElevated: false }));
        if (!_running)
        {
            _ = PumpAsync();
        }
    }

    public void Cancel(OperationItem item)
    {
        item.CancelRequested = true;
        if (_elevatedBatch is not null && _elevatedBatch.Contains(item))
        {
            _elevation?.Cancel(_elevatedBatch.IndexOf(item));
            return;
        }

        if (item.State == OpState.Running)
        {
            item.Cts?.Cancel();
        }
        else if (item.State == OpState.Waiting)
        {
            Complete(item, OpState.Cancelled, "Cancelled");
        }
    }

    public void CancelAll()
    {
        foreach (var i in Active.ToList())
        {
            Cancel(i);
        }
    }

    public void Retry(OperationItem item)
    {
        Finished.Remove(item);
        Enqueue([item.Request], elevate: false);
    }

    private async Task PumpAsync()
    {
        _running = true;
        try
        {
            while (_pending.Count > 0)
            {
                while (IsPaused)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                }

                var (items, elevate) = _pending.Dequeue();
                var todo = items.Where(i => !i.CancelRequested).ToList();
                if (todo.Count == 0)
                {
                    continue;
                }

                var ranElevated = false;
                if (elevate && _elevation is not null)
                {
                    ElevatedBatchRunning = true;
                    _elevatedBatch = todo;
                    try
                    {
                        ranElevated = await _elevation.RunBatchAsync(
                            todo.Select(i => i.Request).ToList(),
                            ev => Ui.Post(() => Apply(todo, ev)),
                            CancellationToken.None).ConfigureAwait(true);
                    }
                    finally
                    {
                        ElevatedBatchRunning = false;
                        _elevatedBatch = null;
                    }

                    // Let the helper's last posted events land, then anything it never reached counts as cancelled.
                    await Task.Delay(250).ConfigureAwait(true);
                    foreach (var left in todo.Where(i => i.State is OpState.Waiting or OpState.Running && ranElevated))
                    {
                        Complete(left, OpState.Cancelled, "Cancelled");
                    }
                }

                if (!ranElevated)
                {
                    foreach (var item in todo)
                    {
                        while (IsPaused)
                        {
                            await Task.Delay(300).ConfigureAwait(true);
                        }

                        if (item.CancelRequested || item.State != OpState.Waiting)
                        {
                            continue;
                        }

                        using var cts = new CancellationTokenSource();
                        item.Cts = cts;
                        var index = todo.IndexOf(item);
                        await Task.Run(() => BatchRunner.RunOneAsync(_pm, item.Request, index, ev => Ui.Post(() => Apply(todo, ev)), cts.Token)).ConfigureAwait(true);
                        item.Cts = null;
                    }
                }
            }
        }
        finally
        {
            _running = false;
            QueueEmptied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Applies a progress/result event to its item (UI thread).</summary>
    internal void Apply(List<OperationItem> batch, BatchEvent ev)
    {
        if (ev.Index < 0 || ev.Index >= batch.Count)
        {
            return;
        }

        var item = batch[ev.Index];
        if (item.State is OpState.Done or OpState.Failed or OpState.Cancelled or OpState.Skipped)
        {
            return;
        }

        switch (ev.State)
        {
            case OpState.Running:
                item.State = OpState.Running;
                if (ev.Phase.Length > 0)
                {
                    item.Phase = ev.Phase;
                }

                if (ev.Percent >= 0 || ev.Phase.Length > 0)
                {
                    item.Percent = ev.Percent;
                }

                break;
            default:
                Complete(item, ev.State, ev.Message);
                break;
        }
    }

    private void Complete(OperationItem item, OpState state, string message)
    {
        item.State = state;
        var firstLine = message.Split('\n')[0];
        item.Message = state switch
        {
            OpState.Done => item.Request.Kind switch
            {
                OpKind.Install => "Installed " + item.Request.Version,
                OpKind.Update => "Updated to " + item.Request.Version,
                _ => "Uninstalled",
            },
            _ => firstLine,
        };
        item.Log = message;
        item.Phase = string.Empty;
        Active.Remove(item);
        Finished.Insert(0, item);
        OnPropertyChanged(nameof(LeftCount));

        if (state != OpState.Cancelled)
        {
            var verb = item.Request.Kind switch { OpKind.Install => "Install", OpKind.Update => "Update", _ => "Uninstall" };
            _history.Add(new HistoryEntry
            {
                Time = DateTimeOffset.Now,
                Id = item.Id,
                Name = item.Name,
                Kind = state == OpState.Done ? HistoryKind.Success : state == OpState.Skipped ? HistoryKind.Info : HistoryKind.Failure,
                Action = state switch
                {
                    OpState.Done => item.Request.Kind switch { OpKind.Install => "Installed", OpKind.Update => "Updated", _ => "Uninstalled" },
                    OpState.Skipped => verb + " skipped",
                    _ => verb + " failed",
                },
                Detail = state == OpState.Done
                    ? item.Request.Kind == OpKind.Update && item.Request.FromVersion.Length > 0 ? item.Request.FromVersion + " → " + item.Request.Version : item.Request.Version
                    : firstLine,
            });
        }

        ItemFinished?.Invoke(this, item);
    }
}
