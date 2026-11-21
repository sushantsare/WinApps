using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PipelineCheck;

public sealed partial class SettingsPage : Page
{
    private readonly bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        var current = App.Window?.CurrentTheme ?? "system";
        ThemeChoice.SelectedIndex = current switch { "light" => 0, "dark" => 1, _ => 2 };
        _ready = true;
    }

    // Each option applies the moment it is checked (mouse, keyboard or UI Automation).
    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is RadioButton { Tag: string theme })
        {
            App.Window?.ApplyTheme(theme);
        }
    }
}
