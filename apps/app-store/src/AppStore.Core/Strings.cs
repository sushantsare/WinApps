using System.Globalization;

namespace AppStore.Core;

/// <summary>All text the code builds at runtime, kept in one place (XAML holds the static labels).</summary>
public static class Strings
{
    private static readonly CultureInfo Ui = CultureInfo.CurrentCulture;

    public const string NotFromWinget = "Not from winget";
    public const string CheckingForUpdates = "Checking for updates…";
    public const string Never = "never";

    public static string Plural(int n, string one, string many) =>
        n == 1 ? $"1 {one}" : string.Create(Ui, $"{n:N0} {many}");

    public static string UpdatesSubtitle(int count, DateTimeOffset? checkedAt, long totalBytes)
    {
        var parts = new List<string> { count == 0 ? "No other updates" : Plural(count, "update", "updates"), "checked " + Ago(checkedAt) };
        if (totalBytes > 0)
        {
            parts.Add(Size(totalBytes));
        }

        return string.Join(" · ", parts);
    }

    public static string Ago(DateTimeOffset? t)
    {
        if (t is null)
        {
            return Never;
        }

        var d = DateTimeOffset.Now - t.Value;
        if (d.TotalSeconds < 60)
        {
            return "just now";
        }

        if (d.TotalMinutes < 60)
        {
            return Plural((int)d.TotalMinutes, "minute", "minutes") + " ago";
        }

        if (d.TotalHours < 24)
        {
            return Plural((int)d.TotalHours, "hour", "hours") + " ago";
        }

        return t.Value.ToLocalTime().ToString("d MMM, h:mm tt", Ui);
    }

    public static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        double mb = bytes / 1024d / 1024d;
        if (mb >= 1024)
        {
            return string.Create(Ui, $"{mb / 1024:0.0} GB");
        }

        return mb >= 10 ? string.Create(Ui, $"{mb:0} MB") : mb >= 1 ? string.Create(Ui, $"{mb:0.#} MB") : string.Create(Ui, $"{Math.Max(1, bytes / 1024)} KB");
    }

    public static string SelectedOf(int selected, int total) => string.Create(Ui, $"{selected} of {total} selected");

    public static string UpdateAll(int n) => string.Create(Ui, $"Update all ({n})");

    public static string InstallSelected(int n) => string.Create(Ui, $"Install selected ({n})");

    public static string UninstallSelected(int n) => string.Create(Ui, $"Uninstall selected ({n})");

    public static string NSelected(int n) => string.Create(Ui, $"{n} selected");

    public static string Results(int n) => Plural(n, "result", "results");

    public static string Apps(int n) => Plural(n, "app", "apps");

    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("h:mm tt", Ui);

    public static string Day(DateTimeOffset t)
    {
        var d = t.ToLocalTime().Date;
        var today = DateTime.Today;
        return d == today ? "Today" : d == today.AddDays(-1) ? "Yesterday" : d.ToString("dddd, d MMMM yyyy", Ui);
    }

    public static string NamesList(IReadOnlyList<string> names)
    {
        if (names.Count <= 1)
        {
            return names.Count == 1 ? names[0] : string.Empty;
        }

        if (names.Count <= 6)
        {
            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        }

        return string.Join(", ", names.Take(5)) + string.Create(Ui, $" and {names.Count - 5} more");
    }

    public static string ExitCodeText(int code) => code == 0 ? "0" : string.Create(CultureInfo.InvariantCulture, $"0x{code:X8}");

    /// <summary>Plain-words meaning of the winget exit codes people actually hit.</summary>
    public static string WingetError(int code) => unchecked((uint)code) switch
    {
        0x8A150011 => "winget couldn't find this package any more.",
        0x8A150014 => "No internet connection, or winget's servers couldn't be reached.",
        0x8A15002B => "No newer version applies to this PC.",
        0x8A150056 => "The app is running. Close it and try again.",
        0x8A150061 => "Another installation is already running. Try again in a minute.",
        0x8A150101 => "The installer is busy with another installation.",
        0x8A150102 => "The installer needs to close an app that is open.",
        0x8A150103 => "A restart is needed before this can install.",
        0x8A150104 => "Not enough disk space.",
        0x8A150105 => "Not enough memory.",
        0x8A150106 => "The installer needs an internet connection.",
        0x8A150107 => "The installer hit an error. Try again later.",
        0x8A150109 => "This app needs a restart to finish installing.",
        0x8A15010A => "The installer was cancelled.",
        0x8A15010B => "A newer version is already installed.",
        0x8A15010C => "Blocked by your organisation's policy.",
        0x8A15010D => "Some apps this one needs failed to install.",
        0x8A150019 => "Windows needs admin permission for this. Turn on \"Ask for admin once per batch\" or accept the Windows popup.",
        0x800704C7 => "Cancelled.",
        _ => "The installer reported an error.",
    };
}
