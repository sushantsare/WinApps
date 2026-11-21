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

    private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && ThemeChoice.SelectedItem is RadioButton { Tag: string theme })
        {
            App.Window?.ApplyTheme(theme);
        }
    }
}
