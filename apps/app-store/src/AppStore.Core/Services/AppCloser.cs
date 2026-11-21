using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace AppStore.Core.Services;

/// <summary>Finds an app's running windows (by its install folder) and closes them politely before an update.</summary>
public static class AppCloser
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Running processes that belong to the app with this display name.</summary>
    public static List<Process> FindRunning(string displayName)
    {
        var result = new List<Process>();
        var folders = InstallFolders(displayName);
        if (folders.Count == 0)
        {
            return result;
        }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.SessionId != Process.GetCurrentProcess().SessionId)
                {
                    p.Dispose();
                    continue;
                }

                var path = p.MainModule?.FileName;
                if (path is not null && folders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(p);
                    continue;
                }
            }
            catch (Win32Exception)
            {
                // Process we may not inspect (system or elevated): not ours.
            }
            catch (InvalidOperationException)
            {
                // Exited meanwhile.
            }

            p.Dispose();
        }

        return result;
    }

    public static bool IsRunning(string displayName)
    {
        var list = FindRunning(displayName);
        var any = list.Count > 0;
        foreach (var p in list)
        {
            p.Dispose();
        }

        return any;
    }

    /// <summary>Asks the app's windows to close (like clicking ✕) and waits up to 10 s. True when nothing is left running.</summary>
    public static async Task<bool> CloseAsync(string displayName, CancellationToken ct)
    {
        var list = FindRunning(displayName);
        try
        {
            foreach (var p in list)
            {
                try
                {
                    p.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                }
            }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (list.All(HasExited))
                {
                    return true;
                }

                await Task.Delay(300, ct).ConfigureAwait(false);
            }

            return list.All(HasExited);
        }
        finally
        {
            foreach (var p in list)
            {
                p.Dispose();
            }
        }
    }

    private static bool HasExited(Process p)
    {
        try
        {
            return p.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Install folders recorded in Windows' "Installed apps" list for entries whose name matches.</summary>
    internal static List<string> InstallFolders(string displayName)
    {
        var folders = new List<string>();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return folders;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var key = root.OpenSubKey(UninstallKey);
                    if (key is null)
                    {
                        continue;
                    }

                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(sub);
                        var name = app?.GetValue("DisplayName") as string;
                        if (name is null || !NameMatches(name, displayName))
                        {
                            continue;
                        }

                        var folder = app!.GetValue("InstallLocation") as string;
                        if (string.IsNullOrWhiteSpace(folder) && app.GetValue("DisplayIcon") is string icon)
                        {
                            var exe = icon.Split(',')[0].Trim('"', ' ');
                            folder = Path.GetDirectoryName(exe);
                        }

                        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder.Trim('"')))
                        {
                            var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim('"'))) + Path.DirectorySeparatorChar;
                            // Never treat a whole drive or Program Files itself as "the app's folder".
                            if (f.Count(c => c == Path.DirectorySeparatorChar) >= 3 && !folders.Contains(f, StringComparer.OrdinalIgnoreCase))
                            {
                                folders.Add(f);
                            }
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
        }

        return folders;
    }

    internal static bool NameMatches(string registryName, string packageName) =>
        registryName.Equals(packageName, StringComparison.OrdinalIgnoreCase) ||
        registryName.StartsWith(packageName + " ", StringComparison.OrdinalIgnoreCase) ||
        packageName.StartsWith(registryName + " ", StringComparison.OrdinalIgnoreCase);
}
