using AppStore.Core.Models;
using AppStore.Pages;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.AppLifecycle;
using Windows.Graphics;
using Windows.System;

namespace AppStore;

public sealed partial class MainWindow : Window
{
    private const int MinWidth = 500;
    private const int MinHeight = 500;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _restoringPane;

    public MainWindow(string[] args)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Our title bar is 48 epx tall: ask Windows for tall caption buttons so ─ □ ✕ are centred in their boxes.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Title = "App Store";

        var settings = App.Services.Settings;
        ApplyTheme(Arg(args, "--theme") ?? settings.Theme);
        RestorePlacement(settings.Window, Arg(args, "--size"));

        Root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        UpdateCaptionButtons();

        var comma = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        comma.Invoked += Settings_Invoked;
        Root.KeyboardAccelerators.Add(comma);

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.IsOpen = false;
        };

        App.Services.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(App.Services.UpdatesCount))
            {
                UpdateBadges();
            }
        };
        App.Services.Queue.Active.CollectionChanged += (_, _) => UpdateBadges();
        App.SettingsVm.AppearanceChanged += (_, name) =>
        {
            if (name == nameof(App.SettingsVm.ThemeIndex))
            {
                ApplyTheme(App.Services.Settings.Theme);
            }
            else
            {
                ApplySidebarSetting();
            }
        };

        AppInstance.GetCurrent().Activated += (_, _) => DispatcherQueue.TryEnqueue(BringToFront);
        Closed += (_, _) => SavePlacement();

        ApplySidebarSetting();
        var start = Arg(args, "--page") ?? settings.StartPage;
        SelectPage(start);
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public void SelectPage(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? NavUpdates;
        Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var page = tag switch
        {
            "search" => typeof(SearchPage),
            "installed" => typeof(InstalledPage),
            "queue" => typeof(QueuePage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(UpdatesPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
        }
    }

    // ---------- sidebar ----------
    private void ApplySidebarSetting()
    {
        var s = App.Services.Settings;
        _restoringPane = true;
        Nav.IsPaneOpen = s.Sidebar switch
        {
            SidebarMode.Open => true,
            SidebarMode.IconsOnly => false,
            _ => s.SidebarOpen,
        };
        _restoringPane = false;
    }

    private void Nav_PaneOpened(NavigationView sender, object args) => RememberPane(true);

    private void Nav_PaneClosed(NavigationView sender, object args) => RememberPane(false);

    private void RememberPane(bool open)
    {
        // Only the wide layout's choice is remembered; narrow windows collapse the menu by themselves.
        if (!_restoringPane && Nav.DisplayMode == NavigationViewDisplayMode.Expanded && App.Services.Settings.SidebarOpen != open)
        {
            App.Services.SettingsStore.Update(s => s.SidebarOpen = open);
        }
    }

    private void Nav_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // Narrow windows: the ☰ lives in the title bar; otherwise at the top of the sidebar.
        var minimal = args.DisplayMode == NavigationViewDisplayMode.Minimal;
        AppTitleBar.IsPaneToggleButtonVisible = minimal;
        Nav.IsPaneToggleButtonVisible = !minimal;
        if (args.DisplayMode == NavigationViewDisplayMode.Expanded)
        {
            ApplySidebarSetting();
        }
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void UpdateBadges()
    {
        var updates = App.Services.UpdatesCount;
        UpdatesBadge.Value = updates;
        UpdatesBadge.Visibility = updates > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationNameFor(NavUpdates, updates > 0 ? "Updates, " + updates + " available" : "Updates");
        var left = App.Services.Queue.Active.Count;
        QueueBadge.Value = left;
        QueueBadge.Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationNameFor(NavQueue, left > 0 ? "Queue, " + left + " left" : "Queue");
    }

    private static void AutomationNameFor(DependencyObject el, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(el, name);

    // ---------- keyboard ----------
    private void Search_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectPage("search");
        DispatcherQueue.TryEnqueue(() => (ContentFrame.Content as SearchPage)?.FocusSearch());
    }

    private void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        switch (ContentFrame.Content)
        {
            case InstalledPage:
                _ = App.Installed.LoadAsync();
                break;
            case SearchPage:
                _ = App.Search.SearchAsync();
                break;
            default:
                SelectPage("updates");
                _ = App.Updates.CheckAsync();
                break;
        }
    }

    private void Settings_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectPage("settings");
    }

    // ---------- toast & crash notice ----------
    public void ShowToast(string message, bool isError)
    {
        Toast.Message = message;
        Toast.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(isError ? 8 : 4);
        _toastTimer.Start();
    }

    public void ShowCrashNotice() =>
        ShowToast("Something went wrong, but App Store kept running. Details are in Settings › Diagnostics › Open log folder.", isError: true);

    // ---------- theme ----------
    public void ApplyTheme(string theme) =>
        Root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

    private void UpdateCaptionButtons()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E) : Windows.UI.Color.FromArgb(0xFF, 0x75, 0x75, 0x75);
        tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
        tb.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
    }

    // ---------- window size & position ----------
    private double Scale
    {
        get
        {
            var dpi = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
    }

    private void RestorePlacement(WindowPlacement? p, string? sizeArg)
    {
        var scale = Scale;
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = (int)(MinWidth * scale);
            op.PreferredMinimumHeight = (int)(MinHeight * scale);
        }

        if (sizeArg is not null && sizeArg.Split('x') is [var w, var h] && int.TryParse(w, out var sw) && int.TryParse(h, out var sh))
        {
            AppWindow.Resize(new SizeInt32((int)(sw * scale), (int)(sh * scale)));
            return;
        }

        if (p is { Width: > 0, Height: > 0 })
        {
            // Only restore if the saved spot is still on a connected monitor.
            var area = DisplayArea.GetFromRect(new RectInt32(p.X, p.Y, p.Width, p.Height), DisplayAreaFallback.None);
            if (area is not null)
            {
                AppWindow.MoveAndResize(new RectInt32(p.X, p.Y, Math.Max(p.Width, (int)(MinWidth * scale)), Math.Max(p.Height, (int)(MinHeight * scale))));
                if (p.Maximized && AppWindow.Presenter is OverlappedPresenter max)
                {
                    max.Maximize();
                }

                return;
            }
        }

        AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(800 * scale)));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - AppWindow.Size.Width) / 2), work.Y + Math.Max(0, (work.Height - AppWindow.Size.Height) / 2)));
    }

    private void SavePlacement()
    {
        if (App.TestMode)
        {
            return;
        }

        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        App.Services.SettingsStore.Update(s =>
        {
            s.Window = maximized && s.Window is not null
                ? new WindowPlacement { X = s.Window.X, Y = s.Window.Y, Width = s.Window.Width, Height = s.Window.Height, Maximized = true }
                : new WindowPlacement { X = pos.X, Y = pos.Y, Width = size.Width, Height = size.Height, Maximized = maximized };
        });
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op)
        {
            op.Restore();
        }

        Activate();
    }
}
