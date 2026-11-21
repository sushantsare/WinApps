using Microsoft.Win32;

namespace AppStore.Core.Services;

/// <summary>
/// Works out where an installed app's icon lives, from Windows' "Installed apps" list (the Uninstall registry keys):
/// ARP\ IDs point at their key directly; winget IDs are matched by app name. Returns the icon file (.exe/.ico/.png),
/// "msix:&lt;package full name&gt;" for Store-style packages, or empty when nothing is known.
/// </summary>
public static class IconSources
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Lock Sync = new();
    private static List<Entry>? _entries;
    private static DateTimeOffset _builtAt;

    private sealed record Entry(string Key, string Scope, string Arch, string Name, string Icon);

    public static string Find(string id, string name)
    {
        if (id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase))
        {
            return "msix:" + id[5..];
        }

        var entries = Entries();
        if (id.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase))
        {
            // ARP\<Machine|User>\<X64|X86|Arm64>\<key name>
            var parts = id.Split('\\', 4);
            if (parts.Length == 4)
            {
                var exact = entries.FirstOrDefault(e => e.Key.Equals(parts[3], StringComparison.OrdinalIgnoreCase) &&
                                                        e.Scope.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                            ?? entries.FirstOrDefault(e => e.Key.Equals(parts[3], StringComparison.OrdinalIgnoreCase));
                if (exact is not null)
                {
                    return exact.Icon;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Icon
               ?? entries.FirstOrDefault(e => AppCloser.NameMatches(e.Name, name))?.Icon
               ?? string.Empty;
    }

    /// <summary>"C:\x\app.exe",0 → C:\x\app.exe (only when the file exists).</summary>
    public static string CleanIconPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            s = end > 0 ? s[1..end] : s.Trim('"');
        }
        else
        {
            var comma = s.LastIndexOf(',');
            if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out _))
            {
                s = s[..comma];
            }
        }

        s = s.Trim();
        var ext = Path.GetExtension(s).ToUpperInvariant();
        if (ext is not (".EXE" or ".ICO" or ".PNG" or ".DLL"))
        {
            return string.Empty;
        }

        return File.Exists(s) ? s : string.Empty;
    }

    private static List<Entry> Entries()
    {
        lock (Sync)
        {
            if (_entries is not null && DateTimeOffset.Now - _builtAt < TimeSpan.FromMinutes(5))
            {
                return _entries;
            }

            var list = new List<Entry>();
            if (OperatingSystem.IsWindows())
            {
                foreach (var (hive, scope) in new[] { (RegistryHive.CurrentUser, "User"), (RegistryHive.LocalMachine, "Machine") })
                {
                    foreach (var (view, arch) in new[] { (RegistryView.Registry64, "X64"), (RegistryView.Registry32, "X86") })
                    {
                        Read(list, hive, scope, view, arch);
                    }
                }
            }

            _entries = list;
            _builtAt = DateTimeOffset.Now;
            return list;
        }
    }

    private static void Read(List<Entry> list, RegistryHive hive, string scope, RegistryView view, string arch)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(UninstallKey);
            if (key is null)
            {
                return;
            }

            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                if (app?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var icon = CleanIconPath(app.GetValue("DisplayIcon") as string ?? string.Empty);
                if (icon.Length == 0 && app.GetValue("InstallLocation") is string folder && Directory.Exists(folder.Trim('"')))
                {
                    // No icon recorded: use the only/likely exe in the install folder.
                    icon = GuessExe(folder.Trim('"'), name);
                }

                if (icon.Length > 0 && !list.Any(e => e.Key == sub && e.Scope == scope && e.Name == name))
                {
                    list.Add(new Entry(sub, scope, arch, name, icon));
                }
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static string GuessExe(string folder, string name)
    {
        try
        {
            var exes = Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                            !Path.GetFileName(f).Contains("update", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exes.Count == 1)
            {
                return exes[0];
            }

            var first = name.Split(' ')[0];
            return exes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(first, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
