using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AppStore.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (App.Services.Health is null)
            {
                await App.Services.CheckHealthAsync().ConfigureAwait(true);
            }
        };
    }

    public SettingsViewModel ViewModel => App.SettingsVm;

    private async void WhatsNew_Click(object sender, RoutedEventArgs e) =>
        await App.Services.Dialogs.ShowMessageAsync("What's new in App Store " + ViewModel.AppVersion, WhatsNew.Text).ConfigureAwait(true);
}

/// <summary>Release notes shown in Settings › About (kept in step with CHANGELOG.md).</summary>
internal static class WhatsNew
{
    public const string Text =
        "1.0.1\n" +
        "• Queue, History and Settings no longer get cut off when the sidebar is open\n" +
        "• New \"Checking for updates\" screen that shows which step is running\n\n" +
        "1.0.0 — first version\n\n" +
        "• Updates page: every pending winget update, Update all with one admin popup, Skip version, Ignore app, release notes\n" +
        "• Search: find any winget app, tick several and install them together, pick a version\n" +
        "• Installed: all your apps, uninstall one or many, per-app install options\n" +
        "• Install options per app: Just me / All users, architecture, install folder, stay on a version, skip major updates, close the app first, extra switches\n" +
        "• Queue with live progress, cancel and retry; History of everything, with export\n" +
        "• Export / import your app list for a new PC\n" +
        "• Fix winget button when Windows Package Manager breaks\n" +
        "• Collapsible sidebar, light and dark themes";
}
