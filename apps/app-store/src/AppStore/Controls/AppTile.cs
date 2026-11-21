using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AppStore.Controls;

/// <summary>The app's real icon when one is known, otherwise a rounded square with the app's first letter, coloured from its package ID (same colour every time).</summary>
public sealed partial class AppTile : Grid
{
    public static readonly DependencyProperty PackageIdProperty =
        DependencyProperty.Register(nameof(PackageId), typeof(string), typeof(AppTile), new PropertyMetadata(string.Empty, (d, _) => ((AppTile)d).Refresh()));

    public static readonly DependencyProperty PackageNameProperty =
        DependencyProperty.Register(nameof(PackageName), typeof(string), typeof(AppTile), new PropertyMetadata(string.Empty, (d, _) => ((AppTile)d).Refresh()));

    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(nameof(IconPath), typeof(string), typeof(AppTile), new PropertyMetadata(string.Empty, (d, _) => ((AppTile)d).LoadIcon()));

    public static readonly DependencyProperty TileSizeProperty =
        DependencyProperty.Register(nameof(TileSize), typeof(double), typeof(AppTile), new PropertyMetadata(36d, (d, _) => ((AppTile)d).Refresh()));

    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0xE8, 0x45, 0x3C), Color.FromArgb(255, 0xF0, 0x8A, 0x24), Color.FromArgb(255, 0x3A, 0x9D, 0x4B),
        Color.FromArgb(255, 0x2F, 0x6F, 0xDF), Color.FromArgb(255, 0x7A, 0x5A, 0xF8), Color.FromArgb(255, 0x0E, 0x9F, 0xA8),
        Color.FromArgb(255, 0xC2, 0x3B, 0x8A), Color.FromArgb(255, 0x8A, 0x6D, 0x3B), Color.FromArgb(255, 0x2B, 0x88, 0xD8),
        Color.FromArgb(255, 0x5B, 0x5F, 0xC7), Color.FromArgb(255, 0xD1, 0x4C, 0x00), Color.FromArgb(255, 0x5A, 0x7D, 0x2C),
    ];

    private readonly Border _border = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private int _loadVersion;
    private bool _hasImage;
    private readonly TextBlock _letter = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Bold };

    public AppTile()
    {
        _border.Child = _letter;
        Children.Add(_border);
        Children.Add(_image);
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        ActualThemeChanged += (_, _) => Refresh();
        Refresh();
    }

    public string PackageId
    {
        get => (string)GetValue(PackageIdProperty);
        set => SetValue(PackageIdProperty, value);
    }

    public string PackageName
    {
        get => (string)GetValue(PackageNameProperty);
        set => SetValue(PackageNameProperty, value);
    }

    /// <summary>Cached icon file (PNG/ICO/JPEG); empty or unreadable = letter tile.</summary>
    public string IconPath
    {
        get => (string)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    private async void LoadIcon()
    {
        var version = ++_loadVersion;
        var path = IconPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowImage(false);
            return;
        }

        try
        {
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical, DecodePixelWidth = (int)Math.Ceiling(TileSize) };
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var w = new Windows.Storage.Streams.DataWriter(ms))
            {
                w.WriteBytes(bytes);
                await w.StoreAsync().AsTask().ConfigureAwait(true);
                w.DetachStream();
            }

            ms.Seek(0);
            await bmp.SetSourceAsync(ms).AsTask().ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return; // a newer icon was requested while this one loaded (recycled list row)
            }

            _image.Source = bmp;
            ShowImage(true);
        }
#pragma warning disable CA1031 // A broken icon file just means the letter tile stays.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (version == _loadVersion)
            {
                ShowImage(false);
            }
        }
    }

    private void ShowImage(bool show)
    {
        _hasImage = show;
        _image.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _image.Source = null;
        }

        Refresh();
    }

    private void Refresh()
    {
        var size = TileSize;
        Width = Height = size;
        _image.Width = _image.Height = Math.Round(size * 0.86);
        _image.HorizontalAlignment = HorizontalAlignment.Center;
        _image.VerticalAlignment = VerticalAlignment.Center;
        _border.Visibility = _hasImage ? Visibility.Collapsed : Visibility.Visible;
        _border.CornerRadius = new CornerRadius(size >= 32 ? 8 : 6);
        var name = string.IsNullOrWhiteSpace(PackageName) ? PackageId ?? "?" : PackageName;
        _letter.Text = name.Length > 0 ? char.ToUpperInvariant(name.TrimStart()[0]).ToString() : "?";
        _letter.FontSize = Math.Round(size * 0.45);

        var color = Palette[StableHash(PackageId ?? name) % Palette.Length];
        var dark = ActualTheme == ElementTheme.Dark;
        _border.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x40 : (byte)0x1F, color.R, color.G, color.B));
        _letter.Foreground = new SolidColorBrush(dark ? Lighten(color) : color);
        if (dark)
        {
            _border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
            _border.BorderThickness = new Thickness(1);
        }
        else
        {
            _border.BorderThickness = new Thickness(0);
        }
    }

    private static Color Lighten(Color c) =>
        Color.FromArgb(255, (byte)(c.R + ((255 - c.R) * 0.35)), (byte)(c.G + ((255 - c.G) * 0.35)), (byte)(c.B + ((255 - c.B) * 0.35)));

    /// <summary>FNV-1a, so the colour is the same on every run (string.GetHashCode is randomised).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in s.ToUpperInvariant())
            {
                h = (h ^ c) * 16777619u;
            }

            return (int)(h & 0x7FFFFFFF);
        }
    }
}
