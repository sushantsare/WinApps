namespace AppStore.Core.Models;

public enum InstallMode
{
    Silent,
    Interactive,
}

public enum CloseAppsMode
{
    Ask,
    Auto,
    Never,
}

public enum SidebarMode
{
    RememberLast,
    Open,
    IconsOnly,
}

/// <summary>All settings, saved as JSON in the app's local data folder.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    // Updates
    public bool CheckOnOpen { get; set; } = true;
    public bool ShowHeldMajorUpdates { get; set; } = true;
    public DateTimeOffset? LastCheck { get; set; }

    /// <summary>Package ID → version the user chose to skip.</summary>
    public Dictionary<string, string> SkippedVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Installing
    public bool AdminOncePerBatch { get; set; } = true;
    public InstallMode InstallMode { get; set; } = InstallMode.Silent;

    /// <summary>"" = let the installer decide, "user", "machine".</summary>
    public string DefaultScope { get; set; } = string.Empty;

    /// <summary>"" = automatic, "x64", "arm64".</summary>
    public string DefaultArchitecture { get; set; } = string.Empty;

    public CloseAppsMode CloseApps { get; set; } = CloseAppsMode.Ask;
    public bool RetryOnce { get; set; } = true;

    /// <summary>Per-app install options, keyed by package ID.</summary>
    public Dictionary<string, PackageOptions> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Start & sidebar
    public string StartPage { get; set; } = "updates";
    public SidebarMode Sidebar { get; set; } = SidebarMode.RememberLast;
    public bool SidebarOpen { get; set; } = true;

    // Search
    public List<string> RecentSearches { get; set; } = [];

    /// <summary>Fetch app icons for search results from each app's website (cached on disk).</summary>
    public bool DownloadSearchIcons { get; set; } = true;

    // History: days to keep, 0 = forever
    public int HistoryDays { get; set; } = 365;

    // Appearance: "system", "light", "dark"
    public string Theme { get; set; } = "system";

    public WindowPlacement? Window { get; set; }

    public PackageOptions OptionsFor(string id) =>
        Options.TryGetValue(id, out var o) ? o : new PackageOptions();

    public void SetOptions(string id, PackageOptions options)
    {
        if (options.IsDefault)
        {
            Options.Remove(id);
        }
        else
        {
            Options[id] = options;
        }
    }
}
