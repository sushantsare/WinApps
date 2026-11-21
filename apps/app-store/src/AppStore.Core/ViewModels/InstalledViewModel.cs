using System.Collections.ObjectModel;
using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStore.Core.ViewModels;

public sealed partial class InstalledViewModel : ObservableObject
{
    private readonly AppServices _s;
    private List<PackageItem> _all = [];
    private bool _loaded;

    public InstalledViewModel(AppServices services)
    {
        _s = services;
        _s.Queue.ItemFinished += (_, op) =>
        {
            var item = _all.FirstOrDefault(a => a.Id.Equals(op.Id, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.InQueue = false;
            }

            if (op.State == OpState.Done)
            {
                _ = LoadAsync();
            }
        };
        _s.SettingsStore.Changed += (_, _) => Ui.Post(RefreshTags);
    }

    public ObservableCollection<PackageItem> Visible { get; } = [];

    [ObservableProperty]
    public partial PageState State { get; set; } = PageState.Loading;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>0 = all sources, 1 = winget, 2 = not from winget.</summary>
    [ObservableProperty]
    public partial int SourceFilter { get; set; }

    [ObservableProperty]
    public partial PackageItem? Selected { get; set; }

    [ObservableProperty]
    public partial int CheckedCount { get; set; }

    public PackageOptions SelectedOptions => Selected is null ? new PackageOptions() : _s.Settings.OptionsFor(Selected.Id);

    public string Subtitle => _all.Count == 0 ? string.Empty : Strings.Apps(_all.Count) + " · " + _all.Count(a => a.IsFromWinget).ToString(System.Globalization.CultureInfo.CurrentCulture) + " from winget";

    public string UninstallSelectedText => Strings.UninstallSelected(CheckedCount);

    public string SelectedText => Strings.NSelected(CheckedCount);

    public string CheckedNames => Strings.NamesList(_all.Where(r => r.IsChecked).Select(r => r.Name).ToList());

    public IReadOnlyList<PackageItem> All => _all;

    public async Task LoadIfNeededAsync()
    {
        if (!_loaded)
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _loaded = true;
        if (_all.Count == 0)
        {
            State = PageState.Loading;
        }

        try
        {
            var list = await Task.Run(() => _s.Pm.GetInstalledAsync(CancellationToken.None)).ConfigureAwait(true);
            var ticked = _all.Where(a => a.IsChecked).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var a in _all)
            {
                a.PropertyChanged -= OnItemChanged;
            }

            _all = list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _s.Remember(_all);
            _s.InstalledIds.Clear();
            foreach (var a in _all)
            {
                _s.InstalledIds.Add(a.Id);
                a.IsChecked = ticked.Contains(a.Id);
                a.InQueue = _s.Queue.Contains(a.Id);
                a.PropertyChanged += OnItemChanged;
            }

            RefreshTags();
            ApplyFilter();
            _s.Icons?.Fill(_all, online: false);
            State = _all.Count == 0 ? PageState.Empty : PageState.Ready;
        }
        catch (WingetException ex)
        {
            ErrorText = ex.Message;
            State = PageState.Error;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorText = "Couldn't read installed apps: " + ex.Message;
            State = PageState.Error;
        }

        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSourceFilterChanged(int value) => ApplyFilter();

    public bool HasSelected => Selected is not null;

    partial void OnSelectedChanged(PackageItem? value)
    {
        OnPropertyChanged(nameof(SelectedOptions));
        OnPropertyChanged(nameof(HasSelected));
    }

    public void ApplyFilter()
    {
        var f = Filter.Trim();
        var list = _all.Where(a =>
            (SourceFilter == 0 || (SourceFilter == 1) == a.IsFromWinget) &&
            (f.Length == 0 || a.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase) || a.Id.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var sel = Selected?.Id;
        Visible.Clear();
        foreach (var a in list)
        {
            Visible.Add(a);
        }

        Selected = Visible.FirstOrDefault(v => v.Id == sel) ?? Visible.FirstOrDefault();
        Recount();
    }

    private void RefreshTags()
    {
        foreach (var a in _all)
        {
            a.Tag = AppServices.TagFor(_s.Settings.OptionsFor(a.Id));
        }

        OnPropertyChanged(nameof(SelectedOptions));
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
        CheckedCount = _all.Count(a => a.IsChecked);
        OnPropertyChanged(nameof(UninstallSelectedText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(CheckedNames));
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var a in _all)
        {
            a.IsChecked = false;
        }
    }

    [RelayCommand]
    public async Task UninstallOneAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is not null && !item.InQueue)
        {
            await _s.ConfirmAndQueueAsync(OpKind.Uninstall, [item]).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public Task UninstallSelectedAsync() =>
        _s.ConfirmAndQueueAsync(OpKind.Uninstall, _all.Where(a => a.IsChecked && !a.InQueue).ToList());

    [RelayCommand]
    public async Task EditOptionsAsync(PackageItem? item)
    {
        item ??= Selected;
        if (item is not null && await _s.EditOptionsAsync(item).ConfigureAwait(true))
        {
            RefreshTags();
        }
    }

    [RelayCommand]
    public async Task ExportAsync()
    {
        if (!_loaded || _all.Count == 0)
        {
            await LoadAsync().ConfigureAwait(true);
        }

        var path = await _s.Dialogs.PickSaveFileAsync("AppStore-list-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ".json", "App Store app list").ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, AppListIo.Serialize(_all, _s.Settings)).ConfigureAwait(true);
            _s.Dialogs.Toast("Exported " + Strings.Apps(_all.Count(a => a.IsFromWinget)) + " to " + Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _s.Dialogs.ShowMessageAsync("Couldn't save the file", ex.Message).ConfigureAwait(true);
        }
    }
}
