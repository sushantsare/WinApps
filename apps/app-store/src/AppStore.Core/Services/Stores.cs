using System.Text.Json;
using AppStore.Core.Models;

namespace AppStore.Core.Services;

/// <summary>Where App Store keeps its own files: %LOCALAPPDATA%\Sushant\AppStore.</summary>
public static class AppPaths
{
    public static string Root { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sushant", "AppStore");

    public static string Logs => Path.Combine(Root, "Logs");

    public static string Batches => Path.Combine(Root, "Batches");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string HistoryFile => Path.Combine(Root, "history.json");
}

/// <summary>Loads and saves settings atomically (write temp file, then replace).</summary>
public sealed class SettingsStore
{
    private readonly string _file;
    private readonly Lock _gate = new();

    public SettingsStore(string? file = null)
    {
        _file = file ?? AppPaths.SettingsFile;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_gate)
        {
            AtomicFile.Write(_file, JsonSerializer.Serialize(Current, AppJson.Default.AppSettings));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(_file), AppJson.Default.AppSettings);
                if (s is not null)
                {
                    // JSON gives case-sensitive dictionaries; winget IDs are case-insensitive.
                    s.Options = new Dictionary<string, PackageOptions>(s.Options, StringComparer.OrdinalIgnoreCase);
                    s.SkippedVersions = new Dictionary<string, string>(s.SkippedVersions, StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch (JsonException)
        {
            // A damaged settings file must never stop the app from opening: keep a copy and start fresh.
            TryBackup();
        }
        catch (IOException)
        {
            TryBackup();
        }

        return new AppSettings();
    }

    private void TryBackup()
    {
        try
        {
            File.Copy(_file, _file + ".broken", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>History of everything App Store installed, updated, uninstalled or skipped.</summary>
public sealed class HistoryStore
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly List<HistoryEntry> _entries;

    public HistoryStore(string? file = null)
    {
        _file = file ?? AppPaths.HistoryFile;
        _entries = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.OrderByDescending(e => e.Time).ToList();
            }
        }
    }

    public void Add(HistoryEntry e)
    {
        lock (_gate)
        {
            _entries.Add(e);
            Persist();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Persist();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops entries older than the chosen number of days (0 = keep forever).</summary>
    public void Prune(int days)
    {
        if (days <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var cutoff = DateTimeOffset.Now.AddDays(-days);
            if (_entries.RemoveAll(e => e.Time < cutoff) > 0)
            {
                Persist();
            }
        }
    }

    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder("Time,App,ID,Action,Detail\r\n");
        foreach (var e in Entries)
        {
            sb.Append(Csv(e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(e.Name)).Append(',').Append(Csv(e.Id)).Append(',').Append(Csv(e.Action)).Append(',').Append(Csv(e.Detail)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private void Persist() => AtomicFile.Write(_file, JsonSerializer.Serialize(_entries, AppJson.Default.ListHistoryEntry));

    private List<HistoryEntry> Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize(File.ReadAllText(_file), AppJson.Default.ListHistoryEntry) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}

internal static class AtomicFile
{
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Reads and writes exported app list files.</summary>
public static class AppListIo
{
    public static string Serialize(IEnumerable<PackageItem> apps, AppSettings settings)
    {
        var file = new AppListFile
        {
            Exported = DateTimeOffset.Now,
            Computer = Environment.MachineName,
            Apps = apps.Where(a => a.IsFromWinget)
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new AppListEntry
                {
                    Id = a.Id,
                    Name = a.Name,
                    Version = a.Version,
                    Options = settings.Options.TryGetValue(a.Id, out var o) ? o : null,
                })
                .ToList(),
        };
        return JsonSerializer.Serialize(file, AppJson.Default.AppListFile);
    }

    /// <summary>Reads an app list file; throws <see cref="InvalidDataException"/> with a plain message when it isn't one.</summary>
    public static AppListFile Parse(string json)
    {
        try
        {
            var f = JsonSerializer.Deserialize(json, AppJson.Default.AppListFile);
            if (f is null || f.Format != "AppStore.AppList" || f.Apps.Count == 0)
            {
                throw new InvalidDataException("This file isn't an App Store app list, or it has no apps in it.");
            }

            f.Apps = f.Apps.Where(a => !string.IsNullOrWhiteSpace(a.Id)).ToList();
            return f;
        }
        catch (JsonException)
        {
            throw new InvalidDataException("This file isn't an App Store app list, or it is damaged.");
        }
    }
}
