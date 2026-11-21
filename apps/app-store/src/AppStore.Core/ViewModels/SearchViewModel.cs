using System.Collections.ObjectModel;
using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStore.Core.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _s;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailsCts;

    public SearchViewModel(AppServices services)
    {
        _s = services;
        foreach (var r in _s.Settings.RecentSearches)
        {
            Recent.Add(r);
        }

        _s.Queue.ItemFinished += OnItemFinished;
    }

    public ObservableCollection<PackageItem> Results { get; } = [];

    public void Dispose()
    {
        _searchCts?.Dispose();
        _detailsCts?.Dispose();
    }

    public ObservableCollection<string> Recent { get; } = [];

    public ObservableCollection<string> Versions { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PageState State { get; set; } = PageState.Start;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LoadingText { get; set; } = "Searching winget…";

    [ObservableProperty]
    public partial PackageItem? Selected { get; set; }

    [ObservableProperty]
    public partial PackageDetails Details { get; set; } = PackageDetails.Empty;

    [ObservableProperty]
    public partial bool DetailsLoading { get; set; }

    /// <summary>Version chosen in the details panel; empty = latest.</summary>
    [ObservableProperty]
    public partial string ChosenVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CheckedCount { get; set; }

    public string ResultsText => State == PageState.Ready ? Strings.Results(Results.Count) : string.Empty;

    public string InstallSelectedText => Strings.InstallSelected(CheckedCount);

    public string SelectedText => Strings.NSelected(CheckedCount);

    public string CheckedNames => Strings.NamesList(Results.Where(r => r.IsChecked).Select(r => r.Name).ToList());

    public string NoResultsText => "No apps found for \"" + LastQuery + "\"";

    [RelayCommand]
    public async Task SearchAsync()
    {
        var q = Query.Trim();
        _searchCts?.Cancel();
        if (q.Length == 0)
        {
            Results.Clear();
            State = PageState.Start;
            OnPropertyChanged(nameof(ResultsText));
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        State = PageState.Loading;
        LastQuery = q;
        LoadingText = "Searching winget for \"" + q + "\"…";
        _ = SlowNoticeAsync(cts.Token);
        try
        {
            var list = await Task.Run(() => _s.Pm.SearchAsync(q, cts.Token)).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var ticked = Results.Where(r => r.IsChecked).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Results)
            {
                r.PropertyChanged -= OnItemChanged;
            }

            Results.Clear();
            foreach (var p in list)
            {
                p.IsInstalled = _s.InstalledIds.Contains(p.Id);
                p.InQueue = _s.Queue.Contains(p.Id);
                p.IsChecked = ticked.Contains(p.Id) && !p.IsInstalled;
                p.PropertyChanged += OnItemChanged;
                Results.Add(p);
            }

            _s.Remember(list);
            AddRecent(q);
            _s.Icons?.Fill([.. Results], online: _s.Settings.DownloadSearchIcons);
            State = Results.Count == 0 ? PageState.Empty : PageState.Ready;
            Selected = Results.FirstOrDefault();
            Recount();
        }
        catch (OperationCanceledException)
        {
        }
        catch (WingetException ex)
        {
            ErrorText = ex.Message;
            State = PageState.Error;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorText = "Search didn't work: " + ex.Message;
            State = PageState.Error;
        }

        OnPropertyChanged(nameof(ResultsText));
        OnPropertyChanged(nameof(NoResultsText));
    }

    private async Task SlowNoticeAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct).ConfigureAwait(true);
            if (State == PageState.Loading)
            {
                LoadingText = "Still searching — winget is probably updating its app list. This happens every few hours and takes a few seconds.";
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    public Task SearchRecentAsync(string? text)
    {
        Query = text ?? string.Empty;
        return SearchAsync();
    }

    private void AddRecent(string q)
    {
        var existing = Recent.FirstOrDefault(r => r.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        Recent.Insert(0, q);
        while (Recent.Count > 6)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        _s.SettingsStore.Update(s => s.RecentSearches = Recent.ToList());
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageItem.IsChecked))
        {
            Recount();
        }
    }

    private void Recount()
    {
        CheckedCount = Results.Count(r => r.IsChecked);
        OnPropertyChanged(nameof(InstallSelectedText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(CheckedNames));
    }

    public bool HasSelected => Selected is not null;

    partial void OnSelectedChanged(PackageItem? value)
    {
        OnPropertyChanged(nameof(HasSelected));
        if (value is not null)
        {
            _s.Icons?.Fill([value], online: _s.Settings.DownloadSearchIcons);
        }

        _ = LoadDetailsAsync(value);
    }

    private async Task LoadDetailsAsync(PackageItem? item)
    {
        _detailsCts?.Cancel();
        Details = PackageDetails.Empty;
        Versions.Clear();
        ChosenVersion = string.Empty;
        if (item is null)
        {
            return;
        }

        var cts = _detailsCts = new CancellationTokenSource();
        DetailsLoading = true;
        try
        {
            var d = await Task.Run(() => _s.Pm.GetDetailsAsync(item.Id, null, cts.Token)).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Details = d ?? PackageDetails.Empty;
            var versions = await Task.Run(() => _s.Pm.GetVersionsAsync(item.Id, cts.Token)).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
            {
                foreach (var v in versions.Take(40))
                {
                    Versions.Add(v);
                }
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
    public async Task InstallOneAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is null || item.IsInstalled || item.InQueue)
        {
            return;
        }

        var version = item == Selected && ChosenVersion.Length > 0 && Versions.Count > 0 && ChosenVersion != Versions[0] ? ChosenVersion : null;
        await _s.ConfirmAndQueueAsync(OpKind.Install, [item], _ => version).ConfigureAwait(true);
        Recount();
    }

    [RelayCommand]
    public async Task InstallSelectedAsync()
    {
        await _s.ConfirmAndQueueAsync(OpKind.Install, Results.Where(r => r.IsChecked && !r.IsInstalled && !r.InQueue).ToList()).ConfigureAwait(true);
        Recount();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var r in Results)
        {
            r.IsChecked = false;
        }
    }

    private void OnItemFinished(object? sender, OperationItem op)
    {
        var item = Results.FirstOrDefault(r => r.Id.Equals(op.Id, StringComparison.OrdinalIgnoreCase));
        if (op.State == OpState.Done && op.Request.Kind == OpKind.Install)
        {
            _s.InstalledIds.Add(op.Id);
        }

        if (item is null)
        {
            return;
        }

        item.InQueue = false;
        item.IsInstalled = _s.InstalledIds.Contains(op.Id);
    }
}
