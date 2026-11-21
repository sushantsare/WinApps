using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStore.Core.Models;

/// <summary>One app in any list (Updates, Search, Installed). Bound directly by the list templates.</summary>
public sealed partial class PackageItem : ObservableObject
{
    public PackageItem(string id, string name)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
    }

    /// <summary>winget package ID (or an ARP\... ID for apps not from winget).</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>Installed version (Updates/Installed) or latest version (Search).</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Newer version offered by winget (Updates only).</summary>
    public string Available { get; init; } = string.Empty;

    /// <summary>"winget", "msstore" or empty for apps winget doesn't manage.</summary>
    public string Source { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public bool IsFromWinget => Source.Length > 0;

    public string SourceText => Source.Length > 0 ? Source : Strings.NotFromWinget;

    /// <summary>Name for search results' second line: "Publisher · ID".</summary>
    public string SubLine => Publisher.Length > 0 ? $"{Publisher} · {Id}" : Id;

    public bool IsMajorJump => VersionCompare.IsMajorJump(Version, Available);

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    public partial bool InQueue { get; set; }

    /// <summary>Download size like "41 MB", or empty while unknown.</summary>
    [ObservableProperty]
    public partial string SizeText { get; set; } = string.Empty;

    public long? SizeBytes { get; set; }

    /// <summary>Small chip text on the Installed page ("Ignored", "Skip major updates"…), empty for none.</summary>
    [ObservableProperty]
    public partial string Tag { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Id}) {Version}";
}
