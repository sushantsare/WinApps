using System.Collections.ObjectModel;
using AppStore.Core.Models;
using AppStore.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStore.Core.ViewModels;

public sealed partial class QueueViewModel : ObservableObject
{
    private readonly AppServices _s;

    public QueueViewModel(AppServices services)
    {
        _s = services;
        Queue.Active.CollectionChanged += (_, _) => Changed();
        Queue.Finished.CollectionChanged += (_, _) => Changed();
        Queue.PropertyChanged += (_, _) => Changed();
    }

    public OperationQueue Queue => _s.Queue;

    public bool HasActive => Queue.Active.Count > 0;

    public bool HasFinished => Queue.Finished.Count > 0;

    public bool IsEmpty => !HasActive && !HasFinished;

    public string Subtitle =>
        HasActive
            ? Strings.Plural(Queue.Active.Count, "item", "items") + " left" + (Queue.ElevatedBatchRunning ? " · admin approved once for this batch" : string.Empty) + (Queue.IsPaused ? " · paused" : string.Empty)
            : HasFinished ? "All done" : string.Empty;

    public string PauseText => Queue.IsPaused ? "Resume queue" : "Pause queue";

    private void Changed()
    {
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(PauseText));
    }

    [RelayCommand]
    public void TogglePause() => Queue.IsPaused = !Queue.IsPaused;

    [RelayCommand]
    public async Task CancelAllAsync()
    {
        if (await _s.Dialogs.ConfirmAsync("Cancel everything in the queue?", "The app being installed now is stopped, and nothing else will start.", "Cancel all").ConfigureAwait(true))
        {
            Queue.CancelAll();
        }
    }

    [RelayCommand]
    public void Cancel(OperationItem? item)
    {
        if (item is not null)
        {
            Queue.Cancel(item);
        }
    }

    [RelayCommand]
    public void Retry(OperationItem? item)
    {
        if (item is not null)
        {
            Queue.Retry(item);
        }
    }

    [RelayCommand]
    public Task ShowDetailsAsync(OperationItem? item) =>
        item is null ? Task.CompletedTask : _s.Dialogs.ShowMessageAsync(item.Name + " — what happened", item.Log.Length > 0 ? item.Log : item.Message);

    [RelayCommand]
    public void ClearFinished() => Queue.Finished.Clear();
}

public sealed class HistoryRow
{
    public HistoryRow(HistoryEntry e) => Entry = e;

    public HistoryEntry Entry { get; }

    public string Name => Entry.Name;

    public string Id => Entry.Id;

    public string Action => Entry.Action;

    public string Detail => Entry.Detail;

    public string TimeText => Strings.Time(Entry.Time);

    public bool IsSuccess => Entry.Kind == HistoryKind.Success;

    public bool IsFailure => Entry.Kind == HistoryKind.Failure;

    public bool IsInfo => Entry.Kind == HistoryKind.Info;
}

public sealed class HistoryGroup : List<HistoryRow>
{
    public HistoryGroup(string title, IEnumerable<HistoryRow> rows)
        : base(rows) => Title = title;

    public string Title { get; }
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _s;

    public HistoryViewModel(AppServices services)
    {
        _s = services;
        _s.History.Changed += (_, _) => Ui.Post(Refresh);
    }

    public ObservableCollection<HistoryGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>0 all, 1 succeeded, 2 failed, 3 skipped/ignored.</summary>
    [ObservableProperty]
    public partial int ResultFilter { get; set; }

    public bool IsEmpty => Groups.Count == 0;

    public bool HasAny => _s.History.Entries.Count > 0;

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnResultFilterChanged(int value) => Refresh();

    public void Refresh()
    {
        var f = Filter.Trim();
        var rows = _s.History.Entries
            .Where(e => ResultFilter switch
            {
                1 => e.Kind == HistoryKind.Success,
                2 => e.Kind == HistoryKind.Failure,
                3 => e.Kind == HistoryKind.Info,
                _ => true,
            })
            .Where(e => f.Length == 0 || e.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase) || e.Id.Contains(f, StringComparison.OrdinalIgnoreCase) || e.Action.Contains(f, StringComparison.CurrentCultureIgnoreCase))
            .Select(e => new HistoryRow(e));
        Groups.Clear();
        foreach (var g in rows.GroupBy(r => Strings.Day(r.Entry.Time)))
        {
            Groups.Add(new HistoryGroup(g.Key, g));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAny));
    }

    [RelayCommand]
    public async Task ExportAsync()
    {
        var path = await _s.Dialogs.PickSaveFileAsync("AppStore-history", ".csv", "CSV file").ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, _s.History.ToCsv()).ConfigureAwait(true);
            _s.Dialogs.Toast("History exported to " + Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _s.Dialogs.ShowMessageAsync("Couldn't save the file", ex.Message).ConfigureAwait(true);
        }
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        _s.SettingsStore.Changed += (_, _) => Ui.Post(RaiseLists);
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppServices.Health))
            {
                OnPropertyChanged(nameof(WingetStatus));
                OnPropertyChanged(nameof(WingetOk));
                OnPropertyChanged(nameof(WingetBad));
            }
        };
    }

    private AppSettings S => _s.Settings;

    public string AppVersion { get; set; } = "1.0.0";

    public bool CheckOnOpen { get => S.CheckOnOpen; set => Set(s => s.CheckOnOpen = value); }

    public bool ShowHeldMajorUpdates { get => S.ShowHeldMajorUpdates; set => Set(s => s.ShowHeldMajorUpdates = value); }

    public bool AdminOncePerBatch { get => S.AdminOncePerBatch; set => Set(s => s.AdminOncePerBatch = value); }

    public int InstallModeIndex { get => (int)S.InstallMode; set => Set(s => s.InstallMode = (InstallMode)Math.Clamp(value, 0, 1)); }

    public int DefaultScopeIndex
    {
        get => S.DefaultScope switch { "user" => 1, "machine" => 2, _ => 0 };
        set => Set(s => s.DefaultScope = value switch { 1 => "user", 2 => "machine", _ => string.Empty });
    }

    public int DefaultArchitectureIndex
    {
        get => S.DefaultArchitecture switch { "x64" => 1, "arm64" => 2, _ => 0 };
        set => Set(s => s.DefaultArchitecture = value switch { 1 => "x64", 2 => "arm64", _ => string.Empty });
    }

    public int CloseAppsIndex { get => (int)S.CloseApps; set => Set(s => s.CloseApps = (CloseAppsMode)Math.Clamp(value, 0, 2)); }

    public bool RetryOnce { get => S.RetryOnce; set => Set(s => s.RetryOnce = value); }

    public int StartPageIndex
    {
        get => S.StartPage switch { "search" => 1, "installed" => 2, _ => 0 };
        set => Set(s => s.StartPage = value switch { 1 => "search", 2 => "installed", _ => "updates" });
    }

    public int SidebarIndex { get => (int)S.Sidebar; set => Set(s => s.Sidebar = (SidebarMode)Math.Clamp(value, 0, 2)); }

    public int HistoryIndex
    {
        get => S.HistoryDays switch { 182 => 1, 0 => 2, _ => 0 };
        set
        {
            Set(s => s.HistoryDays = value switch { 1 => 182, 2 => 0, _ => 365 });
            _s.History.Prune(S.HistoryDays);
        }
    }

    public int ThemeIndex
    {
        get => S.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        set => Set(s => s.Theme = value switch { 1 => "light", 2 => "dark", _ => "system" });
    }

    public string IgnoredText
    {
        get
        {
            var names = S.Options.Where(o => o.Value.IgnoreUpdates || o.Value.StayOnCurrentVersion).Select(o => NameFor(o.Key)).ToList();
            return names.Count == 0 ? "None" : Strings.NamesList(names);
        }
    }

    public bool HasIgnored => S.Options.Any(o => o.Value.IgnoreUpdates || o.Value.StayOnCurrentVersion);

    public string SkippedText => S.SkippedVersions.Count == 0 ? "None" : Strings.NamesList(S.SkippedVersions.Select(k => NameFor(k.Key) + " " + k.Value).ToList());

    public bool HasSkipped => S.SkippedVersions.Count > 0;

    public bool WingetOk => _s.Health?.Ok == true;

    public bool WingetBad => _s.Health is { Ok: false };

    public string VersionLine => "Version " + AppVersion;

    public string WingetStatus => _s.Health switch
    {
        null => "Checking winget…",
        { Ok: true } h => "winget is working · version " + h.Version,
        { } h => "winget isn't working: " + h.Problem,
    };

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Raised after a setting the window applies itself (theme, sidebar) changes.</summary>
    public event EventHandler<string>? AppearanceChanged;

    private string NameFor(string id) => _s.NameFor(id);

    private void Set(Action<AppSettings> change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _s.SettingsStore.Update(change);
        OnPropertyChanged(name);
        if (name is nameof(ThemeIndex) or nameof(SidebarIndex))
        {
            AppearanceChanged?.Invoke(this, name);
        }
    }

    private void RaiseLists()
    {
        OnPropertyChanged(nameof(IgnoredText));
        OnPropertyChanged(nameof(HasIgnored));
        OnPropertyChanged(nameof(SkippedText));
        OnPropertyChanged(nameof(HasSkipped));
    }

    [RelayCommand]
    public void ShowIgnoredAgain()
    {
        _s.SettingsStore.Update(s =>
        {
            foreach (var o in s.Options.Values)
            {
                o.IgnoreUpdates = false;
                o.StayOnCurrentVersion = false;
            }

            foreach (var key in s.Options.Where(o => o.Value.IsDefault).Select(o => o.Key).ToList())
            {
                s.Options.Remove(key);
            }
        });
        _s.Dialogs.Toast("Ignored apps will show updates again");
    }

    [RelayCommand]
    public void UndoSkipped()
    {
        _s.SettingsStore.Update(s => s.SkippedVersions.Clear());
        _s.Dialogs.Toast("Skipped versions will show again");
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        if (await _s.Dialogs.ConfirmAsync("Clear history?", "Every history entry will be deleted. This can't be undone.", "Clear history", destructive: true).ConfigureAwait(true))
        {
            _s.History.Clear();
            _s.Dialogs.Toast("History cleared");
        }
    }

    [RelayCommand]
    public async Task ExportListAsync()
    {
        IsBusy = true;
        try
        {
            var apps = await Task.Run(() => _s.Pm.GetInstalledAsync(CancellationToken.None)).ConfigureAwait(true);
            var path = await _s.Dialogs.PickSaveFileAsync("AppStore-list-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ".json", "App Store app list").ConfigureAwait(true);
            if (path is null)
            {
                return;
            }

            await File.WriteAllTextAsync(path, AppListIo.Serialize(apps, S)).ConfigureAwait(true);
            _s.Dialogs.Toast("Exported " + Strings.Apps(apps.Count(a => a.IsFromWinget)) + " to " + Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Winget.WingetException)
        {
            await _s.Dialogs.ShowMessageAsync("Couldn't export the app list", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ImportListAsync()
    {
        var path = await _s.Dialogs.PickOpenFileAsync(".json", "App Store app list").ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        AppListFile file;
        try
        {
            file = AppListIo.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await _s.Dialogs.ShowMessageAsync("Couldn't import this file", ex.Message).ConfigureAwait(true);
            return;
        }

        var chosen = await _s.Dialogs.ChooseImportAsync(file, _s.InstalledIds).ConfigureAwait(true);
        if (chosen is null || chosen.Count == 0)
        {
            return;
        }

        _s.SettingsStore.Update(s =>
        {
            foreach (var e in chosen.Where(e => e.Options is not null))
            {
                s.SetOptions(e.Id, e.Options!);
            }
        });
        var requests = chosen.Select(e => _s.BuildRequest(new PackageItem(e.Id, e.Name) { Source = "winget" }, OpKind.Install)).ToList();
        _s.Queue.Enqueue(requests, _s.ShouldElevate(requests));
        _s.Dialogs.Toast(Strings.Apps(requests.Count) + " added to the queue");
    }

    [RelayCommand]
    public async Task FixWingetAsync() => await RunElevatedAsync("fix-winget", "winget repaired").ConfigureAwait(true);

    [RelayCommand]
    public async Task ResetSourcesAsync() => await RunElevatedAsync("reset-sources", "winget sources reset").ConfigureAwait(true);

    private async Task RunElevatedAsync(string task, string okText)
    {
        IsBusy = true;
        try
        {
            OpResult? r = _s.Elevation is not null
                ? await _s.Elevation.RunElevatedTaskAsync(task, CancellationToken.None).ConfigureAwait(true)
                : task == "fix-winget"
                    ? await _s.Pm.RepairAsync(new Progress<OpProgress>(), CancellationToken.None).ConfigureAwait(true)
                    : await _s.Pm.ResetSourcesAsync(CancellationToken.None).ConfigureAwait(true);
            if (r is null)
            {
                _s.Dialogs.Toast("Windows' admin popup was declined, so nothing changed.", error: true);
                return;
            }

            _s.Dialogs.Toast(r.Success ? okText : r.Message, error: !r.Success);
            await _s.CheckHealthAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        _s.Dialogs.OpenUri(AppPaths.Logs);
    }

    [RelayCommand]
    public void Email() => _s.Dialogs.OpenUri("mailto:sushant.sare@gmail.com?subject=App%20Store");
}
