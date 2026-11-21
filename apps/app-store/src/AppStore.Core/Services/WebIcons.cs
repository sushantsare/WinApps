namespace AppStore.Core.Services;

/// <summary>Downloads an app's icon from its website (apple-touch-icon.png, then favicon.ico).</summary>
public static class WebIcons
{
    private static readonly HttpClient Http = CreateClient();

    public static string Folder => Path.Combine(AppPaths.Root, "Icons");

    /// <summary>Cache file for an app: Icons\&lt;hash&gt;.img (image) or .none (nothing found; retried after a week).</summary>
    public static string CacheFile(string id, string ext) => Path.Combine(Folder, Hash(id) + ext);

    public static async Task<byte[]?> DownloadAsync(string homepage, CancellationToken ct)
    {
        if (!Uri.TryCreate(homepage, UriKind.Absolute, out var page) || (page.Scheme != Uri.UriSchemeHttps && page.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var host = new Uri(page.GetLeftPart(UriPartial.Authority));
        foreach (var path in new[] { "/apple-touch-icon.png", "/apple-touch-icon-precomposed.png", "/favicon.ico" })
        {
            var bytes = await TryGetAsync(new Uri(host, path), ct).ConfigureAwait(false);
            if (bytes is not null)
            {
                return bytes;
            }
        }

        // Sites like github.io pages keep icons per project; one more try next to the homepage itself.
        return page.AbsolutePath.Length > 1 ? await TryGetAsync(new Uri(page, "favicon.ico"), ct).ConfigureAwait(false) : null;
    }

    /// <summary>True for PNG, ICO, JPEG and GIF data that's big enough to be a real icon.</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> b) =>
        b.Length >= 64 && (
            (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||
            (b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) ||
            (b[0] == 0xFF && b[1] == 0xD8) ||
            (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46));

    private static async Task<byte[]?> TryGetAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > 1_000_000)
            {
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return bytes.Length <= 1_000_000 && LooksLikeImage(bytes) ? bytes : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppStore/1.1");
        return c;
    }

    private static string Hash(string s)
    {
        unchecked
        {
            var h = 14695981039346656037ul;
            foreach (var c in s.ToUpperInvariant())
            {
                h = (h ^ c) * 1099511628211ul;
            }

            return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
