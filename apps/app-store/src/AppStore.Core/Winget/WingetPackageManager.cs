using System.Collections.Concurrent;
using AppStore.Core.Models;

namespace AppStore.Core.Winget;

/// <summary>
/// Real winget backend. Lists come from the WinGet PowerShell module when it is installed (exact data) and from
/// winget.exe tables otherwise; installs, updates and uninstalls always run winget.exe.
/// </summary>
public sealed class WingetPackageManager : IPackageManager
{
    private static readonly string[] Common = ["--accept-source-agreements", "--disable-interactivity"];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OpTimeout = TimeSpan.FromMinutes(45);

    private readonly WingetPowerShell _ps = new();
    private readonly ConcurrentDictionary<string, long?> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PackageDetails> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _listGate = new(1, 1);
    private List<PackageItem>? _installedCache;
    private DateTimeOffset _installedAt;
    private Task? _moduleSetup;

    public static string WingetExe
    {
        get
        {
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            return File.Exists(alias) ? alias : "winget.exe";
        }
    }

    public async Task<WingetHealth> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            var r = await ProcessRunner.RunAsync(WingetExe, ["--version"], TimeSpan.FromSeconds(25), null, ct).ConfigureAwait(false);
            var v = r.Output.Trim();
            if (r.TimedOut)
            {
                return new WingetHealth { Problem = "winget didn't answer within 25 seconds." };
            }

            return r.ExitCode == 0 && v.StartsWith('v')
                ? new WingetHealth { Ok = true, Version = v.TrimStart('v') }
                : new WingetHealth { Problem = "winget returned an error (" + Strings.ExitCodeText(r.ExitCode) + ")." };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new WingetHealth { Problem = "winget isn't installed on this PC." };
        }
    }

    public async Task<List<PackageItem>> GetUpdatesAsync(CancellationToken ct)
    {
        var all = await LoadInstalledAsync(force: true, ct).ConfigureAwait(false);
        if (all is not null)
        {
            return all.Where(p => p.Available.Length > 0 && p.IsFromWinget).ToList();
        }

        var r = await ProcessRunner.RunAsync(WingetExe, ["upgrade", "--source", "winget", .. Common], ListTimeout, null, ct).ConfigureAwait(false);
        if (r.Output.Contains("No applicable", StringComparison.OrdinalIgnoreCase) || r.Output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        ThrowIfFailed(r, allowNonZero: true);
        return WingetParsers.ParseUpgradeTable(r.Output);
    }

    public async Task<List<PackageItem>> GetInstalledAsync(CancellationToken ct)
    {
        var all = await LoadInstalledAsync(force: false, ct).ConfigureAwait(false);
        if (all is not null)
        {
            return all;
        }

        var r = await ProcessRunner.RunAsync(WingetExe, ["list", .. Common], ListTimeout, null, ct).ConfigureAwait(false);
        ThrowIfFailed(r, allowNonZero: true);
        return WingetParsers.ParseListTable(r.Output);
    }

    /// <summary>Full installed list from the PowerShell module (cached for a minute); null when the module isn't usable.</summary>
    private async Task<List<PackageItem>?> LoadInstalledAsync(bool force, CancellationToken ct)
    {
        await _listGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!force && _installedCache is not null && DateTimeOffset.Now - _installedAt < TimeSpan.FromMinutes(1))
            {
                return _installedCache;
            }

            if (!await _ps.IsAvailableAsync(ct).ConfigureAwait(false))
            {
                // First run: set the module up in the background; this time use winget.exe tables.
                _moduleSetup ??= Task.Run(() => _ps.InstallModuleAsync(CancellationToken.None), CancellationToken.None);
                return null;
            }

            var list = await _ps.GetInstalledAsync(ct).ConfigureAwait(false);
            if (list is not null)
            {
                _installedCache = list;
                _installedAt = DateTimeOffset.Now;
            }

            return list;
        }
        finally
        {
            _listGate.Release();
        }
    }

    public async Task<List<PackageItem>> SearchAsync(string query, CancellationToken ct)
    {
        if (await _ps.IsAvailableAsync(ct).ConfigureAwait(false))
        {
            var list = await _ps.SearchAsync(query, ct).ConfigureAwait(false);
            if (list is not null)
            {
                return list;
            }
        }

        var r = await ProcessRunner.RunAsync(WingetExe, ["search", query, "--source", "winget", "--count", "80", .. Common], ListTimeout, null, ct).ConfigureAwait(false);
        if (r.Output.Contains("No package found", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        ThrowIfFailed(r, allowNonZero: true);
        return WingetParsers.ParseSearchTable(r.Output);
    }

    public async Task<PackageDetails?> GetDetailsAsync(string id, string? version, CancellationToken ct)
    {
        if (id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = id + "|" + version;
        if (_details.TryGetValue(key, out var cached))
        {
            return cached;
        }

        List<string> args = ["show", "--id", id, "--exact", "--source", "winget", .. Common];
        if (!string.IsNullOrEmpty(version))
        {
            args.AddRange(["--version", version]);
        }

        var r = await ProcessRunner.RunAsync(WingetExe, args, TimeSpan.FromMinutes(1), null, ct).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            return null;
        }

        var d = WingetParsers.ParseShow(r.Output);
        _details[key] = d;
        return d;
    }

    public async Task<List<string>> GetVersionsAsync(string id, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(WingetExe, ["show", "--id", id, "--exact", "--versions", "--source", "winget", .. Common], TimeSpan.FromMinutes(1), null, ct).ConfigureAwait(false);
        return r.ExitCode == 0 ? WingetParsers.ParseVersions(r.Output) : [];
    }

    public async Task<long?> GetDownloadSizeAsync(string id, string version, CancellationToken ct)
    {
        var key = id + "|" + version;
        if (_sizes.TryGetValue(key, out var known))
        {
            return known;
        }

        long? size = null;
        var d = await GetDetailsAsync(id, version, ct).ConfigureAwait(false);
        if (d is not null && Uri.TryCreate(d.InstallerUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, uri);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                size = resp.IsSuccessStatusCode ? resp.Content.Headers.ContentLength : null;
            }
            catch (HttpRequestException)
            {
                size = null;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                size = null;
            }
        }

        _sizes[key] = size;
        return size;
    }

    public async Task<OpResult> RunAsync(OperationRequest request, IProgress<OpProgress> progress, CancellationToken ct)
    {
        var args = BuildArgs(request);
        progress.Report(new OpProgress { Percent = -1, Phase = request.Kind == OpKind.Uninstall ? "Uninstalling…" : "Starting…" });
        var r = await ProcessRunner.RunAsync(WingetExe, args, OpTimeout, line =>
        {
            var p = WingetParsers.ParseProgress(line);
            if (p is not null)
            {
                progress.Report(p);
            }
        }, ct).ConfigureAwait(false);

        _installedCache = null;
        if (r.TimedOut)
        {
            return OpResult.Fail("It took longer than 45 minutes, so it was stopped.", -1, WingetParsers.Tail(r.Output));
        }

        if (r.ExitCode == 0)
        {
            return OpResult.Ok();
        }

        return OpResult.Fail(Strings.WingetError(r.ExitCode) + " (code " + Strings.ExitCodeText(r.ExitCode) + ")", r.ExitCode, WingetParsers.Tail(r.Output));
    }

    /// <summary>winget.exe arguments for one operation, including the app's saved install options.</summary>
    public static List<string> BuildArgs(OperationRequest q)
    {
        var o = q.Options;
        var verb = q.Kind switch { OpKind.Install => "install", OpKind.Update => "upgrade", _ => "uninstall" };
        List<string> a = [verb, "--id", q.Id, "--exact"];
        var isWingetSource = q.Source.Length > 0 && !q.Id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) && !q.Id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase);
        if (isWingetSource && q.Kind != OpKind.Uninstall)
        {
            a.AddRange(["--source", q.Source]);
        }

        a.Add(q.Interactive ? "--interactive" : "--silent");
        if (q.Kind != OpKind.Uninstall)
        {
            a.Add("--accept-package-agreements");
            if (q.Version.Length > 0)
            {
                a.AddRange(["--version", q.Version]);
            }

            if (o.Scope is "user" or "machine")
            {
                a.AddRange(["--scope", o.Scope]);
            }

            if (o.Architecture is "x64" or "arm64" or "x86")
            {
                a.AddRange(["--architecture", o.Architecture]);
            }

            if (o.Location.Length > 0)
            {
                a.AddRange(["--location", o.Location]);
            }

            if (o.CustomArgs.Length > 0)
            {
                a.AddRange(["--custom", o.CustomArgs]);
            }
        }

        if (q.Kind == OpKind.Update)
        {
            a.Add("--include-unknown");
        }

        a.AddRange(Common);
        return a;
    }

    public async Task<OpResult> ResetSourcesAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(WingetExe, ["source", "reset", "--force", "--disable-interactivity"], TimeSpan.FromMinutes(2), null, ct).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            return OpResult.Fail("Resetting winget sources needs admin permission. (code " + Strings.ExitCodeText(r.ExitCode) + ")", r.ExitCode, WingetParsers.Tail(r.Output));
        }

        await ProcessRunner.RunAsync(WingetExe, ["source", "update", "--disable-interactivity"], TimeSpan.FromMinutes(3), null, ct).ConfigureAwait(false);
        return OpResult.Ok("winget sources were reset.");
    }

    public async Task<OpResult> RepairAsync(IProgress<OpProgress> progress, CancellationToken ct)
    {
        progress.Report(new OpProgress { Phase = "Setting up the WinGet PowerShell module…" });
        await _ps.InstallModuleAsync(ct).ConfigureAwait(false);

        progress.Report(new OpProgress { Phase = "Repairing Windows Package Manager…" });
        const string repair =
            "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';Import-Module Microsoft.WinGet.Client;" +
            "Repair-WinGetPackageManager -Latest -Force -AllUsers | Out-Null; 'ok'";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(repair));
        var r = await ProcessRunner.RunAsync(WingetPowerShell.PowerShellExe, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], TimeSpan.FromMinutes(10), null, ct).ConfigureAwait(false);

        progress.Report(new OpProgress { Phase = "Resetting winget sources…" });
        await ResetSourcesAsync(ct).ConfigureAwait(false);

        var health = await CheckHealthAsync(ct).ConfigureAwait(false);
        return health.Ok
            ? OpResult.Ok("winget " + health.Version + " is working.")
            : OpResult.Fail("winget still isn't working: " + health.Problem, r.ExitCode, WingetParsers.Tail(r.Output));
    }

    private static void ThrowIfFailed(ProcessResult r, bool allowNonZero)
    {
        if (r.TimedOut)
        {
            throw new WingetException("winget took too long to answer.");
        }

        if (r.ExitCode != 0 && !(allowNonZero && WingetParsers.ParseTables(r.Output).Count > 0))
        {
            throw new WingetException(Strings.WingetError(r.ExitCode) + " (code " + Strings.ExitCodeText(r.ExitCode) + ")");
        }
    }
}

public sealed class WingetException : Exception
{
    public WingetException()
    {
    }

    public WingetException(string message)
        : base(message)
    {
    }

    public WingetException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
