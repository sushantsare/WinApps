using System.Text;
using System.Text.Json;
using AppStore.Core.Models;

namespace AppStore.Core.Winget;

/// <summary>Package object printed by our PowerShell snippets (Microsoft.WinGet.Client module).</summary>
public sealed class PsPackage
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Available { get; set; }
    public bool Update { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// Reads package lists through Microsoft's WinGet PowerShell module, which talks to winget's COM API in a separate
/// process. Exact, structured data (no truncated IDs) and winget problems can never crash App Store itself.
/// </summary>
public sealed class WingetPowerShell
{
    private const string Prelude =
        "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8;Import-Module Microsoft.WinGet.Client;";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMinutes(3);
    private bool? _available;

    public static string PowerShellExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (_available is bool known)
        {
            return known;
        }

        var r = await RunAsync("if (Get-Module -ListAvailable -Name Microsoft.WinGet.Client) { 'yes' } else { 'no' }", TimeSpan.FromSeconds(40), ct, prelude: false).ConfigureAwait(false);
        _available = r.ExitCode == 0 && r.Output.Contains("yes", StringComparison.Ordinal);
        return _available.Value;
    }

    /// <summary>Installs the module for the current user (no admin). Returns true when it is usable afterwards.</summary>
    public async Task<bool> InstallModuleAsync(CancellationToken ct)
    {
        const string script =
            "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
            "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;" +
            "if (-not (Get-PackageProvider -ListAvailable -Name NuGet -ErrorAction SilentlyContinue | Where-Object { $_.Version -ge '2.8.5.201' })) { Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force | Out-Null };" +
            "Install-Module -Name Microsoft.WinGet.Client -Scope CurrentUser -Force -AllowClobber -Repository PSGallery | Out-Null; 'ok'";
        var r = await RunAsync(script, TimeSpan.FromMinutes(5), ct, prelude: false).ConfigureAwait(false);
        _available = null;
        return r.ExitCode == 0 && await IsAvailableAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<PackageItem>?> GetInstalledAsync(CancellationToken ct)
    {
        const string script =
            "$r=@(Get-WinGetPackage | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Id=$_.Id; Version=[string]$_.InstalledVersion; " +
            "Available=[string]::Join('|',@($_.AvailableVersions)); Update=[bool]$_.IsUpdateAvailable; Source=[string]$_.Source } });" +
            "ConvertTo-Json -InputObject $r -Compress -Depth 3";
        return await QueryAsync(script, ct).ConfigureAwait(false);
    }

    public async Task<List<PackageItem>?> SearchAsync(string query, CancellationToken ct)
    {
        var q = query.Replace("'", "''", StringComparison.Ordinal);
        var script =
            $"$r=@(Find-WinGetPackage -Query '{q}' -Source winget -Count 80 | ForEach-Object {{ [pscustomobject]@{{ Name=$_.Name; Id=$_.Id; Version=[string]$_.Version; Available=''; Update=$false; Source=[string]$_.Source }} }});" +
            "ConvertTo-Json -InputObject $r -Compress -Depth 3";
        return await QueryAsync(script, ct).ConfigureAwait(false);
    }

    private async Task<List<PackageItem>?> QueryAsync(string script, CancellationToken ct)
    {
        var r = await RunAsync(script, QueryTimeout, ct).ConfigureAwait(false);
        if (r.ExitCode != 0 || r.TimedOut)
        {
            return null;
        }

        var json = r.Output.Trim();
        var start = json.IndexOf('[', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        try
        {
            var list = JsonSerializer.Deserialize(json[start..], PsJson.Default.ListPsPackage) ?? [];
            return list.Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .Select(p => new PackageItem(p.Id!.Trim(), p.Name?.Trim() ?? p.Id!)
                {
                    Version = p.Version?.Trim() ?? string.Empty,
                    Available = p.Update ? Newest(p.Available) : string.Empty,
                    Source = p.Source?.Trim() ?? string.Empty,
                })
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Newest(string? joined)
    {
        if (string.IsNullOrWhiteSpace(joined))
        {
            return string.Empty;
        }

        var best = string.Empty;
        foreach (var v in joined.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (best.Length == 0 || VersionCompare.Compare(v, best) > 0)
            {
                best = v;
            }
        }

        return best;
    }

    private static Task<ProcessResult> RunAsync(string script, TimeSpan timeout, CancellationToken ct, bool prelude = true)
    {
        var full = (prelude ? Prelude : string.Empty) + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return ProcessRunner.RunAsync(PowerShellExe, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], timeout, null, ct);
    }
}
