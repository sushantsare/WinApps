using AppStore.Core.Models;
using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;

namespace AppStore;

/// <summary>Small functions used by x:Bind in XAML (no reflection converters, so Native AOT stays happy).</summary>
public static class X
{
    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NotVis(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static Visibility Has(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility HasObj(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NoObj(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Positive(int value) => value > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static bool IsPositive(int value) => value > 0;

    public static Visibility IsLoading(PageState s) => Vis(s == PageState.Loading);

    public static Visibility IsReady(PageState s) => Vis(s == PageState.Ready);

    public static Visibility IsEmpty(PageState s) => Vis(s == PageState.Empty);

    public static Visibility IsError(PageState s) => Vis(s == PageState.Error);

    public static Visibility IsStart(PageState s) => Vis(s == PageState.Start);

    public static bool IsErrorBool(PageState s) => s == PageState.Error;

    public static string Text(string? value) => value ?? string.Empty;

    public static string Concat(string a, string? b) => a + b;

    public static string Arrow(string from, string to) => from + "  →  " + to;

    public static double Percent(double p) => p < 0 ? 0 : p;

    // Details panel helpers (details can be null while loading).
    public static string Publisher(PackageDetails? d, PackageItem? item) =>
        d?.Publisher is { Length: > 0 } p ? p : item?.Publisher ?? string.Empty;

    public static string Notes(PackageDetails? d, bool loading) =>
        loading ? "Loading…" : d?.ReleaseNotes is { Length: > 0 } n ? n : d?.Description is { Length: > 0 } desc ? desc : "The publisher didn't add release notes for this version.";

    public static string Description(PackageDetails? d, bool loading) =>
        loading ? "Loading…" : d?.Description is { Length: > 0 } desc ? desc : "No description.";

    public static string License(PackageDetails? d) => d is null ? "—" : Or(d.License);

    public static string Installer(PackageDetails? d) =>
        d is null ? "—" : Or(string.Join(" · ", new[] { d.InstallerType, d.InstallerScope }.Where(s => s.Length > 0)));

    public static Uri? Homepage(PackageDetails? d) => ToUri(d?.Homepage);

    public static Uri? NotesUrl(PackageDetails? d) => ToUri(d?.ReleaseNotesUrl);

    public static Visibility HomepageVis(PackageDetails? d) => HasObj(Homepage(d));

    public static string HomepageText(PackageDetails? d) =>
        Homepage(d) is { } u ? u.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) + u.AbsolutePath.TrimEnd('/') : string.Empty;

    public static Visibility NotesVis(PackageDetails? d) => HasObj(NotesUrl(d));

    public static string InstallAnywayName(string name) => "Install anyway: " + name;

    public static string InstallName(string name) => "Install " + name;

    public static string CancelName(string name) => "Cancel " + name;

    public static string HistoryName(string name, string action, string time) => name + ", " + action + ", " + time;

    public static string RetryName(string name) => "Retry " + name;

    public static string DetailsName(string name) => "What happened with " + name;

    public static bool CanTick(bool installed, bool inQueue) => !installed && !inQueue;

    public static Visibility CanInstall(bool installed, bool inQueue) => Vis(!installed && !inQueue);

    public static Visibility InstalledVis(bool installed) => Vis(installed);

    public static Visibility NotInstalledVis(bool installed) => Vis(!installed);

    public static string VersionChip(PackageItem? item) => item is null ? string.Empty : "v" + item.Version;

    public static Visibility HasLicense(PackageDetails? d) => Has(d?.License);

    public static string VersionAndSource(PackageItem? item) => item is null ? string.Empty : item.Version + " · " + item.SourceText;

    public static bool FromWinget(PackageItem? item) => item?.IsFromWinget == true;

    public static Visibility VisWinget(PackageItem? item) => Vis(item?.IsFromWinget == true);

    public static Visibility VisNotWinget(PackageItem? item) => Vis(item is { IsFromWinget: false });

    public static string ScopeText(PackageOptions? o) => o?.Scope switch { "user" => "Just me", "machine" => "All users", _ => "Default" };

    public static string ArchText(PackageOptions? o) => o?.Architecture is { Length: > 0 } a ? a : "Automatic";

    public static string UpdatesText(PackageOptions? o) =>
        o is null ? "Normal" : o.IgnoreUpdates ? "Ignored" : o.StayOnCurrentVersion ? "Stay on this version" : o.SkipMajorUpdates ? "Skip major updates" : "Normal";

    public static string CloseText(PackageOptions? o) => o?.CloseBeforeUpdate switch { true => "Yes", false => "No", _ => "Default" };

    private static Uri? ToUri(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u : null;

    public static string WhatsNewTitle(PackageItem? item) => item is null ? string.Empty : "What's new in " + item.Available;

    public static string SelectName(string name) => "Select " + name;

    private static string Or(string s) => s.Length > 0 ? s : "—";
}
