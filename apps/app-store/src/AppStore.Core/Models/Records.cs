namespace AppStore.Core.Models;

/// <summary>Everything "winget show" tells us about one package.</summary>
public sealed class PackageDetails
{
    /// <summary>Placeholder while nothing is loaded (x:Bind skips functions whose argument is null).</summary>
    public static PackageDetails Empty { get; } = new();

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseNotesUrl { get; set; } = string.Empty;
    public string InstallerType { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string InstallerScope { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
}

/// <summary>Per-app install options, remembered and reused for every future update of that app.</summary>
public sealed class PackageOptions
{
    /// <summary>"" = let the installer decide, "user", "machine".</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>"" = automatic, "x64", "arm64", "x86".</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Stay on the installed version (no updates offered).</summary>
    public bool StayOnCurrentVersion { get; set; }

    /// <summary>Custom install folder, empty = installer default.</summary>
    public string Location { get; set; } = string.Empty;

    public bool SkipMajorUpdates { get; set; }

    /// <summary>null = follow the global "Close apps before updating" setting.</summary>
    public bool? CloseBeforeUpdate { get; set; }

    public bool IgnoreUpdates { get; set; }

    /// <summary>Extra installer switches passed with winget --custom.</summary>
    public string CustomArgs { get; set; } = string.Empty;

    public bool IsDefault =>
        Scope.Length == 0 && Architecture.Length == 0 && !StayOnCurrentVersion && Location.Length == 0 &&
        !SkipMajorUpdates && CloseBeforeUpdate is null && !IgnoreUpdates && CustomArgs.Length == 0;

    public PackageOptions Clone() => (PackageOptions)MemberwiseClone();
}

public enum OpKind
{
    Install,
    Update,
    Uninstall,
}

public enum OpState
{
    Waiting,
    Running,
    Done,
    Failed,
    Cancelled,
    Skipped,
}

/// <summary>What to do for one app; serialisable so the elevated helper can run it.</summary>
public sealed class OperationRequest
{
    public OpKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool Interactive { get; set; }
    public bool CloseAppFirst { get; set; }
    public bool RetryOnce { get; set; }
    public PackageOptions Options { get; set; } = new();
}

/// <summary>Progress line the elevated helper writes for the main app.</summary>
public sealed class BatchEvent
{
    public int Index { get; set; }
    public OpState State { get; set; }
    public double Percent { get; set; } = -1;
    public string Phase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class OpResult
{
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Log { get; init; } = string.Empty;

    public static OpResult Ok(string message = "") => new() { Success = true, Message = message };

    public static OpResult Fail(string message, int exitCode = -1, string log = "") =>
        new() { Success = false, Message = message, ExitCode = exitCode, Log = log };

    public static OpResult Skip(string message) => new() { Skipped = true, Message = message };
}

public sealed class OpProgress
{
    public double Percent { get; init; } = -1;
    public string Phase { get; init; } = string.Empty;
}

public enum HistoryKind
{
    Success,
    Failure,
    Info,
}

public sealed class HistoryEntry
{
    public DateTimeOffset Time { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public HistoryKind Kind { get; set; }
}

/// <summary>One app in an exported app list file.</summary>
public sealed class AppListEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public PackageOptions? Options { get; set; }
}

public sealed class AppListFile
{
    public string Format { get; set; } = "AppStore.AppList";
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset Exported { get; set; }
    public string Computer { get; set; } = string.Empty;
    public List<AppListEntry> Apps { get; set; } = [];
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Result of the winget health check.</summary>
public sealed class WingetHealth
{
    public bool Ok { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Problem { get; init; } = string.Empty;
}
