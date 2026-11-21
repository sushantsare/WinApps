using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStore.Core.ViewModels;

/// <summary>Shared services and the rules every page uses to turn ticked apps into queued operations.</summary>
public sealed partial class AppServices : ObservableObject
{
    public AppServices(IPackageManager pm, SettingsStore settings, HistoryStore history, IDialogs dialogs, IElevation? elevation)
    {
        Pm = pm;
        SettingsStore = settings;
        History = history;
        Dialogs = dialogs;
        Elevation = elevation;
        Queue = new OperationQueue(pm, history, elevation);
    }

    public IPackageManager Pm { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings => SettingsStore.Current;

    public HistoryStore History { get; }

    public IDialogs Dialogs { get; }

    public IElevation? Elevation { get; }

    public OperationQueue Queue { get; }

    /// <summary>Real app icons; null in unit tests (letter tiles only).</summary>
    public IIconProvider? Icons { get; set; }

    /// <summary>IDs of installed apps (lower-case), for "Installed" chips in Search.</summary>
    public HashSet<string> InstalledIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Package ID → display name, for Settings lists (options are stored by ID).</summary>
    public Dictionary<string, string> KnownNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(IEnumerable<PackageItem> apps)
    {
        foreach (var a in apps)
        {
            KnownNames[a.Id] = a.Name;
        }
    }

    public string NameFor(string id) => KnownNames.TryGetValue(id, out var n) ? n : id;

    [ObservableProperty]
    public partial WingetHealth? Health { get; set; }

    [ObservableProperty]
    public partial int UpdatesCount { get; set; }

    public async Task<WingetHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        var h = await Pm.CheckHealthAsync(ct).ConfigureAwait(true);
        Health = h;
        return h;
    }

    /// <summary>Install options for an app: its own saved options, with the global defaults filled in.</summary>
    public PackageOptions EffectiveOptions(string id)
    {
        var o = Settings.OptionsFor(id).Clone();
        if (o.Scope.Length == 0)
        {
            o.Scope = Settings.DefaultScope;
        }

        if (o.Architecture.Length == 0)
        {
            o.Architecture = Settings.DefaultArchitecture;
        }

        return o;
    }

    public bool WantsCloseFirst(string id)
    {
        var own = Settings.OptionsFor(id).CloseBeforeUpdate;
        return own ?? Settings.CloseApps != CloseAppsMode.Never;
    }

    public OperationRequest BuildRequest(PackageItem app, OpKind kind, string? version = null)
    {
        var o = EffectiveOptions(app.Id);
        return new OperationRequest
        {
            Kind = kind,
            Id = app.Id,
            Name = app.Name,
            Source = app.Source.Length > 0 ? app.Source : kind == OpKind.Install ? "winget" : string.Empty,
            Version = version ?? (kind == OpKind.Update ? app.Available : string.Empty),
            FromVersion = kind == OpKind.Update ? app.Version : string.Empty,
            Interactive = Settings.InstallMode == InstallMode.Interactive,
            CloseAppFirst = kind != OpKind.Install && WantsCloseFirst(app.Id),
            RetryOnce = Settings.RetryOnce,
            Options = o,
        };
    }

    /// <summary>Whether a batch should run in the elevated helper (one admin popup for everything).</summary>
    public bool ShouldElevate(IReadOnlyList<OperationRequest> requests) =>
        Elevation is { IsElevated: false } && Settings.AdminOncePerBatch &&
        (requests.Count >= 2 || requests.Any(r => r.Options.Scope == "machine"));

    /// <summary>Shows the confirm dialog (with open-app warnings and the admin note) and queues the batch.</summary>
    public async Task<bool> ConfirmAndQueueAsync(OpKind kind, IReadOnlyList<PackageItem> apps, Func<PackageItem, string?>? versionFor = null)
    {
        if (apps.Count == 0)
        {
            return false;
        }

        var requests = apps.Select(a => BuildRequest(a, kind, versionFor?.Invoke(a))).ToList();
        var elevate = ShouldElevate(requests);

        var warnings = new List<string>();
        if (kind != OpKind.Install)
        {
            var running = await Task.Run(() => apps.Where(a => AppCloser.IsRunning(a.Name)).ToList()).ConfigureAwait(true);
            foreach (var a in running)
            {
                var req = requests.First(r => r.Id == a.Id);
                warnings.Add(req.CloseAppFirst
                    ? a.Name + " is open. It will be closed before " + (kind == OpKind.Update ? "updating." : "uninstalling.")
                    : a.Name + " is open. The " + (kind == OpKind.Update ? "update" : "uninstall") + " may fail or ask you to close it.");
            }
        }

        var names = apps.Select(a => a.Name).ToList();
        var (title, body, primary) = kind switch
        {
            OpKind.Update => (apps.Count == 1 ? "Update " + apps[0].Name + "?" : "Update " + Strings.Apps(apps.Count) + "?",
                              apps.Count == 1 ? apps[0].Name + " will update to " + requests[0].Version + "." : Strings.NamesList(names) + " will update one after another.",
                              apps.Count == 1 ? "Update" : "Update " + Strings.Apps(apps.Count)),
            OpKind.Install => (apps.Count == 1 ? "Install " + apps[0].Name + "?" : "Install " + Strings.Apps(apps.Count) + "?",
                               apps.Count == 1 ? apps[0].Name + (requests[0].Version.Length > 0 ? " " + requests[0].Version : string.Empty) + " will be installed from winget." : Strings.NamesList(names) + " will install one after another.",
                               apps.Count == 1 ? "Install" : "Install " + Strings.Apps(apps.Count)),
            _ => (apps.Count == 1 ? "Uninstall " + apps[0].Name + "?" : "Uninstall " + Strings.Apps(apps.Count) + "?",
                  Strings.NamesList(names) + (apps.Count == 1 ? " will be removed from this PC." : " will be removed from this PC, one after another."),
                  "Uninstall"),
        };

        var info = elevate ? "Windows will ask for admin permission once for the whole batch." : null;
        if (!await Dialogs.ConfirmAsync(title, body, primary, warnings, info, destructive: kind == OpKind.Uninstall).ConfigureAwait(true))
        {
            return false;
        }

        Queue.Enqueue(requests, elevate);
        foreach (var a in apps)
        {
            a.InQueue = true;
            a.IsChecked = false;
        }

        Dialogs.Toast(apps.Count == 1 ? apps[0].Name + " added to the queue" : Strings.Apps(apps.Count) + " added to the queue");
        return true;
    }

    public void AddHistoryInfo(PackageItem app, string action, string detail) =>
        History.Add(new HistoryEntry { Time = DateTimeOffset.Now, Id = app.Id, Name = app.Name, Action = action, Detail = detail, Kind = HistoryKind.Info });

    /// <summary>Opens the Install options dialog for an app and saves the result.</summary>
    public async Task<bool> EditOptionsAsync(PackageItem app)
    {
        var versions = app.IsFromWinget ? await Pm.GetVersionsAsync(app.Id, CancellationToken.None).ConfigureAwait(true) : [];
        var result = await Dialogs.EditOptionsAsync(app, Settings.OptionsFor(app.Id).Clone(), versions).ConfigureAwait(true);
        if (result is null)
        {
            return false;
        }

        SettingsStore.Update(s => s.SetOptions(app.Id, result));
        Dialogs.Toast("Install options saved for " + app.Name);
        return true;
    }

    public static string TagFor(PackageOptions o) =>
        o.IgnoreUpdates ? "Ignored"
        : o.StayOnCurrentVersion ? "Stays on this version"
        : o.SkipMajorUpdates ? "Skip major updates"
        : o.IsDefault ? string.Empty
        : "Install options saved";
}
