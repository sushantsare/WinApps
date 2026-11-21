using System.Collections.ObjectModel;
using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStore.Core.ViewModels;

public enum PageState
{
    Loading,
    Ready,
    Empty,
    Error,
    Start,
}

public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _s;
    private List<PackageItem> _raw = [];
    private CancellationTokenSource? _detailsCts;
    private CancellationTokenSource? _sizeCts;
    private bool _loadedOnce;

    public UpdatesViewModel(AppServices services)
    {
        _s = services;
        _s.Queue.ItemFinished += OnItemFinished;
        _s.SettingsStore.Changed += (_, _) => Ui.Post(ApplyFilters);
    }

    public AppServices Services => _s;

    public void Dispose()
    {
        _detailsCts?.Dispose();
        _sizeCts?.Dispose();
    }

    public ObservableCollection<PackageItem> Items { get; } = [];

    /// <summary>Major updates held back by "Skip major updates".</summary>
    public ObservableCollection<PackageItem> Held { get; } = [];

    [ObservableProperty]
    public partial PageState State { get; set; } = PageState.Loading;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsWingetBroken { get; set; }

    [ObservableProperty]
    public partial PackageItem? Selected { get; set; }

    [ObservableProperty]
    public partial PackageDetails? Details { get; set; }

    [ObservableProperty]
    public partial bool DetailsLoading { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = Strings.CheckingForUpdates;

    [ObservableProperty]
    public partial int CheckedCount { get; set; }

    [ObservableProperty]
    public partial bool IsFixing { get; set; }

    public int HiddenCount { get; private set; }

    public string UpdateAllText => Strings.UpdateAll(CheckedCount);

    public string SelectionText => Strings.SelectedOf(CheckedCount, Items.Count);

    /// <summary>Header tick: true = all, false = none, null = some.</summary>
    public bool? AllChecked => CheckedCount == 0 ? false : CheckedCount == Items.Count ? true : null;

    public string UpToDateText => Strings.Apps(_s.InstalledIds.Count) + " checked" + (HiddenCount > 0 ? " · " + Strings.Plural(HiddenCount, "skipped or ignored update", "skipped or ignored updates") + " hidden" : string.Empty);

    public async Task LoadIfNeededAsync()
    {
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            if (_s.Settings.CheckOnOpen || _raw.Count == 0)
            {
                await CheckAsync().ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    public async Task CheckAsync()
    {
        State = PageState.Loading;
        Subtitle = Strings.CheckingForUpdates;
        ErrorText = string.Empty;
        try
        {
            var health = await _s.CheckHealthAsync().ConfigureAwait(true);
            if (!health.Ok)
            {
                IsWingetBroken = true;
                ErrorText = health.Problem;
                State = PageState.Error;
                Subtitle = string.Empty;
                return;
            }

            IsWingetBroken = false;
            var updates = await Task.Run(() => _s.Pm.GetUpdatesAsync(CancellationToken.None)).ConfigureAwait(true);
            _raw = updates;
            _s.Remember(updates);
            _s.SettingsStore.Update(s => s.LastCheck = DateTimeOffset.Now);
            _ = RefreshInstalledIdsAsync();
            ApplyFilters();
        }
        catch (WingetException ex)
        {
            ErrorText = ex.Message;
            State = PageState.Error;
            Subtitle = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorText = "Couldn't read updates from winget: " + ex.Message;
            State = PageState.Error;
            Subtitle = string.Empty;
        }
    }

    private async Task RefreshInstalledIdsAsync()
    {
        try
        {
            var all = await Task.Run(() => _s.Pm.GetInstalledAsync(CancellationToken.None)).ConfigureAwait(true);
            _s.Remember(all);
            _s.InstalledIds.Clear();
            foreach (var a in all)
            {
                _s.InstalledIds.Add(a.Id);
            }

            OnPropertyChanged(nameof(UpToDateText));
        }
        catch (WingetException)
        {
            // Only used for "Installed" chips; the Installed page shows its own error.
        }
    }

    /// <summary>Applies ignore / skipped-version / skip-major rules to the raw winget list.</summary>
    public void ApplyFilters()
    {
        if (State == PageState.Loading && _raw.Count == 0 && !_loadedOnce)
        {
            return;
        }

        var settings = _s.Settings;
        var keepChecked = Items.Where(i => !i.IsChecked).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedId = Selected?.Id;
        var visible = new List<PackageItem>();
        var held = new List<PackageItem>();
        var hidden = 0;
        foreach (var p in _raw.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var o = settings.OptionsFor(p.Id);
            if (o.IgnoreUpdates || o.StayOnCurrentVersion ||
                (settings.SkippedVersions.TryGetValue(p.Id, out var skipped) && VersionCompare.Compare(skipped, p.Available) >= 0))
            {
                hidden++;
                continue;
            }

            if (o.SkipMajorUpdates && p.IsMajorJump)
            {
                if (settings.ShowHeldMajorUpdates)
                {
                    held.Add(p);
                }
                else
                {
                    hidden++;
                }

                continue;
            }

            p.IsChecked = !keepChecked.Contains(p.Id);
            p.InQueue = _s.Queue.Contains(p.Id);
            visible.Add(p);
        }

        HiddenCount = hidden;
        Replace(Items, visible);
        Replace(Held, held);
        foreach (var p in visible)
        {
            p.PropertyChanged -= OnItemChanged;
            p.PropertyChanged += OnItemChanged;
        }

        RecountChecked();
        _s.UpdatesCount = Items.Count;
        State = Items.Count == 0 && Held.Count == 0 ? PageState.Empty : PageState.Ready;
        Selected = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
        UpdateSubtitle();
        OnPropertyChanged(nameof(UpToDateText));
        _ = LoadSizesAsync();
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageItem.IsChecked))
        {
            RecountChecked();
        }
    }

    private void RecountChecked()
    {
        CheckedCount = Items.Count(i => i.IsChecked && !i.InQueue);
        OnPropertyChanged(nameof(UpdateAllText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(AllChecked));
    }

    private void UpdateSubtitle()
    {
        var total = Items.Where(i => i.SizeBytes is > 0).Sum(i => i.SizeBytes!.Value);
        Subtitle = Strings.UpdatesSubtitle(Items.Count, _s.Settings.LastCheck, total);
    }

    private async Task LoadSizesAsync()
    {
        _sizeCts?.Cancel();
        var cts = _sizeCts = new CancellationTokenSource();
        using var gate = new SemaphoreSlim(3);
        var tasks = Items.Concat(Held).Where(i => i.SizeBytes is null).ToList().Select(async item =>
        {
            await gate.WaitAsync(cts.Token).ConfigureAwait(true);
            try
            {
                var size = await Task.Run(() => _s.Pm.GetDownloadSizeAsync(item.Id, item.Available, cts.Token)).ConfigureAwait(true);
                item.SizeBytes = size ?? 0;
                item.SizeText = size is > 0 ? Strings.Size(size.Value) : "—";
            }
            finally
            {
                gate.Release();
            }
        });
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        UpdateSubtitle();
    }

    partial void OnSelectedChanged(PackageItem? value) => _ = LoadDetailsAsync(value);

    private async Task LoadDetailsAsync(PackageItem? item)
    {
        _detailsCts?.Cancel();
        Details = null;
        if (item is null)
        {
            return;
        }

        var cts = _detailsCts = new CancellationTokenSource();
        DetailsLoading = true;
        try
        {
            var d = await Task.Run(() => _s.Pm.GetDetailsAsync(item.Id, item.Available, cts.Token)).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
            {
                Details = d;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                DetailsLoading = false;
            }
        }
    }

    [RelayCommand]
    public void ToggleAll()
    {
        var target = AllChecked != true;
        foreach (var i in Items.Where(i => !i.InQueue))
        {
            i.IsChecked = target;
        }
    }

    [RelayCommand]
    public async Task UpdateAllAsync()
    {
        await _s.ConfirmAndQueueAsync(OpKind.Update, Items.Where(i => i.IsChecked && !i.InQueue).ToList()).ConfigureAwait(true);
        RecountChecked();
    }

    [RelayCommand]
    public async Task UpdateOneAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is not null && !item.InQueue)
        {
            await _s.ConfirmAndQueueAsync(OpKind.Update, [item]).ConfigureAwait(true);
            RecountChecked();
        }
    }

    [RelayCommand]
    public async Task SkipVersionAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        if (await _s.Dialogs.ConfirmAsync(
                "Skip " + item.Name + " " + item.Available + "?",
                "You won't be offered this version again. A newer version will still show up. You can undo this in Settings › Skipped versions.",
                "Skip version").ConfigureAwait(true))
        {
            _s.SettingsStore.Update(s => s.SkippedVersions[item.Id] = item.Available);
            _s.AddHistoryInfo(item, "Skipped version", item.Available);
            _s.Dialogs.Toast(item.Name + " " + item.Available + " skipped");
        }
    }

    [RelayCommand]
    public async Task IgnoreAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        if (await _s.Dialogs.ConfirmAsync(
                "Ignore all updates for " + item.Name + "?",
                "App Store will never show updates for this app. You can undo this in Settings › Ignored apps or in its Install options.",
                "Ignore app").ConfigureAwait(true))
        {
            var o = _s.Settings.OptionsFor(item.Id).Clone();
            o.IgnoreUpdates = true;
            _s.SettingsStore.Update(s => s.SetOptions(item.Id, o));
            _s.AddHistoryInfo(item, "Ignored", "All updates");
            _s.Dialogs.Toast(item.Name + " ignored");
        }
    }

    [RelayCommand]
    public Task EditOptionsAsync(PackageItem? item) => (item ?? Selected) is { } i ? _s.EditOptionsAsync(i) : Task.CompletedTask;

    [RelayCommand]
    public Task InstallHeldAsync(PackageItem? item) =>
        item is null ? Task.CompletedTask : _s.ConfirmAndQueueAsync(OpKind.Update, [item]);

    [RelayCommand]
    public async Task FixWingetAsync()
    {
        IsFixing = true;
        try
        {
            OpResult? r = _s.Elevation is not null
                ? await _s.Elevation.RunElevatedTaskAsync("fix-winget", CancellationToken.None).ConfigureAwait(true)
                : await _s.Pm.RepairAsync(new Progress<OpProgress>(), CancellationToken.None).ConfigureAwait(true);
            if (r is null)
            {
                _s.Dialogs.Toast("Windows' admin popup was declined, so winget wasn't repaired.", isError: true);
                return;
            }

            _s.Dialogs.Toast(r.Success ? "winget repaired" : r.Message, isError: !r.Success);
            await CheckAsync().ConfigureAwait(true);
        }
        finally
        {
            IsFixing = false;
        }
    }

    private void OnItemFinished(object? sender, OperationItem op)
    {
        var item = _raw.FirstOrDefault(p => p.Id.Equals(op.Id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.InQueue = false;
        if (op.State == OpState.Done && op.Request.Kind is OpKind.Update or OpKind.Uninstall)
        {
            _raw.Remove(item);
        }

        ApplyFilters();
    }

    private static void Replace(ObservableCollection<PackageItem> target, List<PackageItem> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }
}
