using AppStore.Core.Models;

namespace AppStore.Core.Winget;

/// <summary>
/// Sample data for UI tests and screenshots (launch flag --test-data). Behaves like winget, including progress and
/// failures, but never touches the PC.
/// </summary>
public sealed class FakePackageManager : IPackageManager
{
    private readonly Lock _gate = new();
    private readonly int _speedMs;
    private readonly List<(string Id, string Name, string Publisher, string Version, string Available, long Size)> _installed =
    [
        ("Google.Chrome", "Google Chrome", "Google LLC", "131.0.6778", "132.0.6834", 117_440_512),
        ("VideoLAN.VLC", "VLC media player", "VideoLAN", "3.0.21", "3.0.22", 42_991_616),
        ("Notepad++.Notepad++", "Notepad++", "Notepad++ Team", "8.7.1", "8.7.4", 5_242_880),
        ("Microsoft.PowerToys", "Microsoft PowerToys", "Microsoft Corporation", "0.86.0", "0.87.1", 260_046_848),
        ("7zip.7zip", "7-Zip", "Igor Pavlov", "24.08", "24.09", 1_572_864),
        ("Python.Python.3.13", "Python 3.13", "Python Software Foundation", "3.13.0", "3.13.1", 28_311_552),
        ("OBSProject.OBSStudio", "OBS Studio", "OBS Project", "30.2.3", "31.0.0", 144_703_488),
        ("Microsoft.Teams", "Microsoft Teams", "Microsoft Corporation", "24335.206", "", 0),
        ("Telegram.TelegramDesktop", "Telegram Desktop", "Telegram FZ-LLC", "5.9.0", "", 0),
        ("ARP\\Machine\\X64\\HPSmart", "HP Smart", "HP Inc.", "151.3.1", "", 0),
    ];

    private readonly List<(string Id, string Name, string Publisher, string Version, string License)> _catalog =
    [
        ("Microsoft.VisualStudioCode", "Visual Studio Code", "Microsoft Corporation", "1.96.2", "MIT"),
        ("Microsoft.VisualStudioCode.Insiders", "VS Code Insiders", "Microsoft Corporation", "1.97.0", "MIT"),
        ("VSCodium.VSCodium", "VSCodium", "VSCodium", "1.96.2", "MIT"),
        ("Anysphere.Cursor", "Cursor", "Anysphere", "0.44.9", "Proprietary"),
        ("SublimeHQ.SublimeText.4", "Sublime Text 4", "Sublime HQ", "4.0.4189", "Proprietary"),
        ("VideoLAN.VLC", "VLC media player", "VideoLAN", "3.0.22", "GPL-2.0"),
        ("OBSProject.OBSStudio", "OBS Studio", "OBS Project", "31.0.0", "GPL-2.0"),
        ("Telegram.TelegramDesktop", "Telegram Desktop", "Telegram FZ-LLC", "5.9.0", "GPL-3.0"),
        ("Python.Python.3.13", "Python 3.13", "Python Software Foundation", "3.13.1", "PSF"),
        ("Brave.Brave", "Brave", "Brave Software", "1.73.97", "MPL-2.0"),
        ("Mozilla.Firefox", "Mozilla Firefox", "Mozilla", "133.0.3", "MPL-2.0"),
        ("Git.Git", "Git", "The Git Development Community", "2.47.1", "GPL-2.0"),
    ];

    public FakePackageManager(int speedMs = 120) => _speedMs = speedMs;

    public bool WingetBroken { get; set; }

    public async Task<WingetHealth> CheckHealthAsync(CancellationToken ct)
    {
        await Task.Delay(_speedMs, ct).ConfigureAwait(false);
        return WingetBroken ? new WingetHealth { Problem = "winget isn't responding." } : new WingetHealth { Ok = true, Version = "1.9.25200" };
    }

    public async Task<List<PackageItem>> GetUpdatesAsync(CancellationToken ct)
    {
        await Task.Delay(_speedMs * 4, ct).ConfigureAwait(false);
        if (WingetBroken)
        {
            throw new WingetException("winget isn't responding.");
        }

        lock (_gate)
        {
            return _installed.Where(p => p.Available.Length > 0)
                .Select(p => new PackageItem(p.Id, p.Name) { Version = p.Version, Available = p.Available, Source = "winget", Publisher = p.Publisher })
                .ToList();
        }
    }

    public async Task<List<PackageItem>> GetInstalledAsync(CancellationToken ct)
    {
        await Task.Delay(_speedMs * 2, ct).ConfigureAwait(false);
        lock (_gate)
        {
            return _installed.Select(p => new PackageItem(p.Id, p.Name)
            {
                Version = p.Version,
                Available = p.Available,
                Source = p.Id.StartsWith("ARP\\", StringComparison.Ordinal) ? string.Empty : "winget",
                Publisher = p.Publisher,
            }).ToList();
        }
    }

    public async Task<List<PackageItem>> SearchAsync(string query, CancellationToken ct)
    {
        await Task.Delay(_speedMs * 2, ct).ConfigureAwait(false);
        var words = query.Replace("vs code", "code", StringComparison.OrdinalIgnoreCase).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _catalog.Where(c => words.All(w => (c.Name + " " + c.Id).Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                                   (query.Contains("code", StringComparison.OrdinalIgnoreCase) && c.Name.Contains("Codium", StringComparison.OrdinalIgnoreCase)))
            .Select(c => new PackageItem(c.Id, c.Name) { Version = c.Version, Source = "winget", Publisher = c.Publisher })
            .ToList();
    }

    public async Task<PackageDetails?> GetDetailsAsync(string id, string? version, CancellationToken ct)
    {
        await Task.Delay(_speedMs, ct).ConfigureAwait(false);
        if (id.StartsWith("ARP\\", StringComparison.Ordinal))
        {
            return null;
        }

        var name = _catalog.FirstOrDefault(c => c.Id == id).Name ?? _installed.FirstOrDefault(c => c.Id == id).Name ?? id;
        var publisher = _catalog.FirstOrDefault(c => c.Id == id).Publisher ?? _installed.FirstOrDefault(c => c.Id == id).Publisher ?? string.Empty;
        var host = id.Split('.')[0].ToLowerInvariant();
        return new PackageDetails
        {
            Id = id,
            Name = name,
            Version = version ?? string.Empty,
            Publisher = publisher,
            Description = id == "Microsoft.VisualStudioCode"
                ? "Code editing. Redefined. Free, built on open source, runs everywhere. Extensions for every language."
                : "A popular Windows app from the winget community repository.",
            Homepage = "https://" + host + ".example.com",
            License = _catalog.FirstOrDefault(c => c.Id == id).License ?? "Proprietary",
            ReleaseNotes = "• Security fixes (14 issues)\n• Faster start-up\n• Bug fixes from the publisher's release notes",
            ReleaseNotesUrl = "https://" + host + ".example.com/releases",
            InstallerType = "exe",
            InstallerScope = "machine",
            InstallerUrl = "https://" + host + ".example.com/setup.exe",
        };
    }

    public async Task<List<string>> GetVersionsAsync(string id, CancellationToken ct)
    {
        await Task.Delay(_speedMs, ct).ConfigureAwait(false);
        var v = _catalog.FirstOrDefault(c => c.Id == id).Version ?? "1.0.0";
        return [v, "1.95.3", "1.94.2", "1.93.1"];
    }

    public async Task<long?> GetDownloadSizeAsync(string id, string version, CancellationToken ct)
    {
        await Task.Delay(_speedMs / 2, ct).ConfigureAwait(false);
        lock (_gate)
        {
            var size = _installed.FirstOrDefault(p => p.Id == id).Size;
            return size > 0 ? size : 102_760_448;
        }
    }

    public async Task<OpResult> RunAsync(OperationRequest request, IProgress<OpProgress> progress, CancellationToken ct)
    {
        for (var p = 0; p <= 100; p += 10)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(request.Kind == OpKind.Uninstall
                ? new OpProgress { Percent = -1, Phase = "Uninstalling…" }
                : p < 70 ? new OpProgress { Percent = p, Phase = $"Downloading… {p}%" } : new OpProgress { Percent = -1, Phase = "Installing…" });
            await Task.Delay(_speedMs, ct).ConfigureAwait(false);
        }

        if (request.Id == "Microsoft.VisualStudioCode.Insiders")
        {
            return OpResult.Fail("The installer reported an error. (code 0x8A150107)", unchecked((int)0x8A150107), "Installer failed with exit code: 1603");
        }

        lock (_gate)
        {
            var i = _installed.FindIndex(p => p.Id == request.Id);
            if (request.Kind == OpKind.Uninstall && i >= 0)
            {
                _installed.RemoveAt(i);
            }
            else if (request.Kind == OpKind.Update && i >= 0)
            {
                var x = _installed[i];
                _installed[i] = (x.Id, x.Name, x.Publisher, x.Available, string.Empty, 0);
            }
            else if (request.Kind == OpKind.Install && i < 0)
            {
                var c = _catalog.FirstOrDefault(c => c.Id == request.Id);
                _installed.Add((request.Id, c.Name ?? request.Name, c.Publisher ?? string.Empty, request.Version.Length > 0 ? request.Version : c.Version ?? "1.0", string.Empty, 0));
            }
        }

        return OpResult.Ok();
    }

    public async Task<OpResult> ResetSourcesAsync(CancellationToken ct)
    {
        await Task.Delay(_speedMs * 3, ct).ConfigureAwait(false);
        return OpResult.Ok("winget sources were reset.");
    }

    public async Task<OpResult> RepairAsync(IProgress<OpProgress> progress, CancellationToken ct)
    {
        progress.Report(new OpProgress { Phase = "Repairing Windows Package Manager…" });
        await Task.Delay(_speedMs * 5, ct).ConfigureAwait(false);
        WingetBroken = false;
        return OpResult.Ok("winget 1.9.25200 is working.");
    }
}
