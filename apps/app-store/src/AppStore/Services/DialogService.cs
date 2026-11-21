using System.Diagnostics;
using AppStore.Core.Models;
using AppStore.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.Windows.Storage.Pickers;

namespace AppStore.Services;

/// <summary>ContentDialogs, file pickers and toasts for the view-models.</summary>
public sealed class DialogService : IDialogs
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private MainWindow? _window;

    public void Attach(MainWindow window) => _window = window;

    private XamlRoot Root => _window!.Content.XamlRoot;

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _one.WaitAsync().ConfigureAwait(true);
        try
        {
            dialog.XamlRoot = Root;
            dialog.RequestedTheme = ((FrameworkElement)_window!.Content).ActualTheme;
            dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
            return await dialog.ShowAsync();
        }
        finally
        {
            _one.Release();
        }
    }

    public async Task<bool> ConfirmAsync(string title, string body, string primary, IReadOnlyList<string>? warnings = null, string? info = null, bool destructive = false)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
        foreach (var w in warnings ?? [])
        {
            panel.Children.Add(new InfoBar { Message = w, Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false });
        }

        if (info is not null)
        {
            panel.Children.Add(new InfoBar { Message = info, Severity = InfoBarSeverity.Informational, IsOpen = true, IsClosable = false });
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, "ConfirmDialog");
        return await ShowAsync(dialog).ConfigureAwait(true) == ContentDialogResult.Primary;
    }

    public async Task<PackageOptions?> EditOptionsAsync(PackageItem app, PackageOptions current, IReadOnlyList<string> versions)
    {
        var view = new Dialogs.InstallOptionsView(app, current, versions, App.Services.Settings, PickFolderAsync);
        var dialog = new ContentDialog
        {
            Title = "Install options",
            Content = view,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Reset to defaults",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, "OptionsDialog");
        var r = await ShowAsync(dialog).ConfigureAwait(true);
        return r switch
        {
            ContentDialogResult.Primary => view.Result(),
            ContentDialogResult.Secondary => new PackageOptions(),
            _ => null,
        };
    }

    public async Task<IReadOnlyList<AppListEntry>?> ChooseImportAsync(AppListFile file, ISet<string> alreadyInstalled)
    {
        var boxes = new List<(CheckBox Box, AppListEntry Entry)>();
        var list = new StackPanel { Spacing = 2 };
        foreach (var e in file.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var installed = alreadyInstalled.Contains(e.Id);
            var cb = new CheckBox
            {
                IsChecked = !installed,
                Content = installed ? e.Name + "  (already installed)" : e.Name,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cb, e.Name);
            boxes.Add((cb, e));
            list.Children.Add(cb);
        }

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = Core.Strings.Apps(file.Apps.Count) + " in this list" + (file.Computer.Length > 0 ? " (from " + file.Computer + ")" : string.Empty) + ". Tick the ones to install.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });
        var dialog = new ContentDialog
        {
            Title = "Import app list",
            Content = panel,
            PrimaryButtonText = "Install selected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await ShowAsync(dialog).ConfigureAwait(true) == ContentDialogResult.Primary
            ? boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Entry).ToList()
            : null;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension, string typeLabel)
    {
        var picker = new FileSavePicker(_window!.AppWindow.Id)
        {
            SuggestedFileName = suggestedName,
            DefaultFileExtension = extension,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add(typeLabel, [extension]);
        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    public async Task<string?> PickOpenFileAsync(string extension, string typeLabel)
    {
        var picker = new FileOpenPicker(_window!.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(_window!.AppWindow.Id);
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    public async Task ShowMessageAsync(string title, string body)
    {
        var text = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = body });
        text.Blocks.Add(p);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = text, MaxHeight = 400 },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await ShowAsync(dialog).ConfigureAwait(true);
    }

    public void Toast(string message, bool isError = false) => _window?.ShowToast(message, isError);

    public void OpenUri(string uri)
    {
        try
        {
            if (Directory.Exists(uri))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { uri }, UseShellExecute = true });
            }
            else
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UriFormatException or InvalidOperationException)
        {
            Toast("Couldn't open " + uri, isError: true);
        }
    }
}
