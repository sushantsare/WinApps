using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace PipelineCheck;

public sealed partial class MainWindow : Window
{
    public MainWindow(string[] args)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // AppWindow sizes are physical pixels: scale the design size by the monitor DPI.
        var dpi = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(720 * scale)));

        var themeIndex = Array.IndexOf(args, "--theme");
        if (themeIndex >= 0 && themeIndex + 1 < args.Length)
        {
            ApplyTheme(args[themeIndex + 1]);
        }

        Nav.SelectedItem = NavHome;
    }

    public void ApplyTheme(string theme) =>
        Root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

    public string CurrentTheme => Root.RequestedTheme switch
    {
        ElementTheme.Light => "light",
        ElementTheme.Dark => "dark",
        _ => "system",
    };

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItem as NavigationViewItem)?.Tag as string switch
        {
            "about" => typeof(AboutPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        ContentFrame.Navigate(page);
    }
}
