using AppStore.Core.Models;

namespace AppStore.Core.Winget;

/// <summary>Everything App Store needs from winget. Real: <see cref="WingetPackageManager"/>; tests/demo: <see cref="FakePackageManager"/>.</summary>
public interface IPackageManager
{
    Task<WingetHealth> CheckHealthAsync(CancellationToken ct);

    /// <summary>Apps with a newer version available (winget-managed only).</summary>
    Task<List<PackageItem>> GetUpdatesAsync(CancellationToken ct);

    /// <summary>Every installed app winget can see, including apps it doesn't manage.</summary>
    Task<List<PackageItem>> GetInstalledAsync(CancellationToken ct);

    Task<List<PackageItem>> SearchAsync(string query, CancellationToken ct);

    Task<PackageDetails?> GetDetailsAsync(string id, string? version, CancellationToken ct);

    Task<List<string>> GetVersionsAsync(string id, CancellationToken ct);

    /// <summary>Download size of the installer, or null when unknown.</summary>
    Task<long?> GetDownloadSizeAsync(string id, string version, CancellationToken ct);

    Task<OpResult> RunAsync(OperationRequest request, IProgress<OpProgress> progress, CancellationToken ct);

    Task<OpResult> ResetSourcesAsync(CancellationToken ct);

    /// <summary>Repairs winget (needs admin; run inside the elevated helper).</summary>
    Task<OpResult> RepairAsync(IProgress<OpProgress> progress, CancellationToken ct);
}
