using System.Collections.Concurrent;
using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace AppStore.Services;

/// <summary>
/// Fills <see cref="PackageItem.IconPath"/> with a cached PNG/ICO of the real app icon:
/// installed programs (exe/ico from the Installed apps list), Store packages (their logo) and,
/// for search results, the icon from the app's website. Everything is cached in %LOCALAPPDATA%\Sushant\AppStore\Icons.
/// </summary>
public sealed class IconProvider(IPackageManager pm, bool allowOnline) : IIconProvider, IDisposable
{
    private const int OnlineLimit = 15;
    private readonly SemaphoreSlim _localGate = new(3, 3);
    private readonly SemaphoreSlim _webGate = new(2, 2);
    private readonly ConcurrentDictionary<string, Task<string>> _work = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        _localGate.Dispose();
        _webGate.Dispose();
    }

    public void Fill(IReadOnlyList<PackageItem> items, bool online)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IconPath.Length > 0)
            {
                continue;
            }

            var useWeb = online && allowOnline && i < OnlineLimit;
            _ = ApplyAsync(item, useWeb);
        }
    }

    private async Task ApplyAsync(PackageItem item, bool useWeb)
    {
        var key = item.Id + (useWeb ? "|web" : string.Empty);
        var task = _work.GetOrAdd(key, _ => Task.Run(() => ResolveAsync(item.Id, item.Name, useWeb)));
        string path;
        try
        {
            path = await task.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An icon is never worth a crash: fall back to the letter tile.
        catch (Exception)
#pragma warning restore CA1031
        {
            _work.TryRemove(key, out _);
            return;
        }

        if (path.Length == 0)
        {
            _work.TryRemove(key, out _); // allow a later retry (e.g. after an install)
            return;
        }

        Ui.Post(() => item.IconPath = path);
    }

    private async Task<string> ResolveAsync(string id, string name, bool useWeb)
    {
        Directory.CreateDirectory(WebIcons.Folder);
        var cached = WebIcons.CacheFile(id, ".img");
        if (File.Exists(cached) && new FileInfo(cached).Length > 0)
        {
            return cached;
        }

        await _localGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var src = IconSources.Find(id, name);
            var bytes = src.StartsWith("msix:", StringComparison.Ordinal) ? await MsixLogoAsync(src[5..]).ConfigureAwait(false)
                : src.Length > 0 ? await FileIconAsync(src).ConfigureAwait(false)
                : null;
            if (bytes is not null)
            {
                await File.WriteAllBytesAsync(cached, bytes).ConfigureAwait(false);
                return cached;
            }
        }
        finally
        {
            _localGate.Release();
        }

        if (!useWeb || id.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var none = WebIcons.CacheFile(id, ".none");
        if (File.Exists(none) && DateTime.Now - File.GetLastWriteTime(none) < TimeSpan.FromDays(7))
        {
            return string.Empty;
        }

        await _webGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var d = await pm.GetDetailsAsync(id, null, CancellationToken.None).ConfigureAwait(false);
            var web = d is null ? null : await WebIcons.DownloadAsync(d.Homepage, CancellationToken.None).ConfigureAwait(false);
            if (web is null)
            {
                await File.WriteAllTextAsync(none, string.Empty).ConfigureAwait(false);
                return string.Empty;
            }

            await File.WriteAllBytesAsync(cached, web).ConfigureAwait(false);
            return cached;
        }
        finally
        {
            _webGate.Release();
        }
    }

    private static async Task<byte[]?> FileIconAsync(string path)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        if (ext is ".ICO" or ".PNG")
        {
            var b = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return WebIcons.LooksLikeImage(b) ? b : null;
        }

        // .exe / .dll: ask Windows for the file's icon, as Explorer shows it.
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 64, ThumbnailOptions.ResizeThumbnail).AsTask().ConfigureAwait(false);
        if (thumb is null || thumb.Size == 0)
        {
            return null;
        }

        using var s = thumb.AsStreamForRead();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static async Task<byte[]?> MsixLogoAsync(string fullName)
    {
        var pkg = new Windows.Management.Deployment.PackageManager().FindPackageForUser(string.Empty, fullName);
        var logo = pkg?.Logo;
        if (logo is null || !logo.IsFile)
        {
            return null;
        }

        var path = logo.LocalPath;
        if (!File.Exists(path))
        {
            // Logo points at "Square44x44Logo.png" but the file on disk is "Square44x44Logo.scale-200.png".
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            path = Directory.Exists(dir)
                ? Directory.GetFiles(dir, stem + "*.png").OrderByDescending(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? string.Empty
                : string.Empty;
        }

        return path.Length > 0 ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
    }
}
