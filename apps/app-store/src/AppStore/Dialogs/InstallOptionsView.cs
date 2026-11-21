using AppStore.Controls;
using AppStore.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AppStore.Dialogs;

/// <summary>Content of the "Install options" dialog (per app, reused for every future update).</summary>
public sealed partial class InstallOptionsView : UserControl
{
    private readonly ComboBox _scope = Combo("OptScope", "Let the installer decide", "Just me", "All users");
    private readonly ComboBox _arch = Combo("OptArchitecture", "Automatic", "x64", "ARM64", "x86");
    private readonly ComboBox _version = Combo("OptVersion", "Always latest", "Stay on the current version");
    private readonly TextBox _location = new() { PlaceholderText = "Installer default", MinWidth = 190 };
    private readonly ToggleSwitch _major = Toggle("OptSkipMajor");
    private readonly ToggleSwitch _close = Toggle("OptCloseFirst");
    private readonly ToggleSwitch _ignore = Toggle("OptIgnore");
    private readonly TextBox _custom = new() { PlaceholderText = "e.g. --no-desktop-icon", Width = 190 };
    private readonly Func<Task<string?>> _pickFolder;

    public InstallOptionsView(PackageItem app, PackageOptions o, IReadOnlyList<string> versions, AppSettings settings, Func<Task<string?>> pickFolder)
    {
        _pickFolder = pickFolder;
        _scope.SelectedIndex = o.Scope switch { "user" => 1, "machine" => 2, _ => 0 };
        _arch.SelectedIndex = o.Architecture switch { "x64" => 1, "arm64" => 2, "x86" => 3, _ => 0 };
        _version.SelectedIndex = o.StayOnCurrentVersion ? 1 : 0;
        _location.Text = o.Location;
        _major.IsOn = o.SkipMajorUpdates;
        _close.IsOn = o.CloseBeforeUpdate ?? settings.CloseApps != CloseAppsMode.Never;
        _ignore.IsOn = o.IgnoreUpdates;
        _custom.Text = o.CustomArgs;
        AutomationProperties.SetName(_location, "Install folder");
        AutomationProperties.SetName(_custom, "Extra installer options");

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new AppTile { PackageId = app.Id, PackageName = app.Name, TileSize = 40 });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = app.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        names.Children.Add(new TextBlock { Text = "Used for every future update", Style = (Style)Application.Current.Resources["SecondaryTextStyle"] });
        header.Children.Add(names);

        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folderRow.Children.Add(_location);
        var browse = new Button { Content = "Browse…" };
        AutomationProperties.SetName(browse, "Choose install folder");
        browse.Click += async (_, _) =>
        {
            var path = await _pickFolder().ConfigureAwait(true);
            if (path is not null)
            {
                _location.Text = path;
            }
        };
        folderRow.Children.Add(browse);

        var latest = versions.Count > 0 ? "Latest is " + versions[0] : string.Empty;
        var panel = new StackPanel { MinWidth = 520 };
        panel.Children.Add(header);
        panel.Children.Add(Row("Install for", _scope, "All users usually needs admin permission"));
        panel.Children.Add(Row("Architecture", _arch, string.Empty));
        panel.Children.Add(Row("Version", _version, latest));
        panel.Children.Add(Row("Install folder", folderRow, "Only some installers allow this"));
        panel.Children.Add(Row("Skip major updates", _major, "Get small fixes, hold back big version jumps"));
        panel.Children.Add(Row("Close the app before updating", _close, "Skips the update if it can't be closed"));
        panel.Children.Add(Row("Ignore all updates for this app", _ignore, string.Empty));
        panel.Children.Add(Row("Extra installer options", _custom, "For advanced use", last: true));
        Content = new ScrollViewer { Content = panel, MaxHeight = 520, Padding = new Thickness(0, 0, 12, 0) };
    }

    public PackageOptions Result() => new()
    {
        Scope = _scope.SelectedIndex switch { 1 => "user", 2 => "machine", _ => string.Empty },
        Architecture = _arch.SelectedIndex switch { 1 => "x64", 2 => "arm64", 3 => "x86", _ => string.Empty },
        StayOnCurrentVersion = _version.SelectedIndex == 1,
        Location = _location.Text.Trim(),
        SkipMajorUpdates = _major.IsOn,
        CloseBeforeUpdate = _close.IsOn,
        IgnoreUpdates = _ignore.IsOn,
        CustomArgs = _custom.Text.Trim(),
    };

    private static ComboBox Combo(string id, params string[] items)
    {
        var c = new ComboBox { MinWidth = 190 };
        foreach (var i in items)
        {
            c.Items.Add(i);
        }

        AutomationProperties.SetAutomationId(c, id);
        return c;
    }

    private static ToggleSwitch Toggle(string id)
    {
        var t = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
        AutomationProperties.SetAutomationId(t, id);
        return t;
    }

    private static Grid Row(string label, FrameworkElement control, string description, bool last = false)
    {
        var g = new Grid { Padding = new Thickness(0, 10, 0, 10), ColumnSpacing = 16 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!last)
        {
            g.BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
            g.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (description.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        }

        AutomationProperties.SetName(control, label);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(text);
        g.Children.Add(control);
        return g;
    }
}
