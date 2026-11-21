using Microsoft.UI.Xaml.Markup;

namespace PipelineCheck;

/// <summary>
/// Diagnostic (run with --xaml-probe): loads each piece of the main window on its own and
/// logs which one fails, with the parser's line/position message. Used by CI when the
/// window fails to load.
/// </summary>
internal static class XamlProbe
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static readonly (string Name, string Xaml)[] Pieces =
    [
        ("Grid", $"<Grid {Ns}/>"),
        ("MicaBackdrop", $"<MicaBackdrop {Ns}/>"),
        ("TitleBar", $"<TitleBar {Ns} Title=\"Probe\"/>"),
        ("TitleBar+AutomationId", $"<TitleBar {Ns} Title=\"Probe\" AutomationProperties.AutomationId=\"X\"/>"),
        ("NavigationView", $"<NavigationView {Ns} IsBackButtonVisible=\"Collapsed\" IsSettingsVisible=\"False\" PaneDisplayMode=\"Auto\"/>"),
        ("NavigationViewItem+SymbolIcon", $"<NavigationViewItem {Ns} Content=\"Home\"><NavigationViewItem.Icon><SymbolIcon Symbol=\"Home\"/></NavigationViewItem.Icon></NavigationViewItem>"),
        ("FontIcon", $"<FontIcon {Ns} Glyph=\"&#xE946;\"/>"),
        ("Frame", $"<Frame {Ns}/>"),
        ("RadioButtons", $"<RadioButtons {Ns} Header=\"Theme\"><RadioButton Content=\"Light\"/></RadioButtons>"),
        ("HeadingLevel", $"<TextBlock {Ns} AutomationProperties.HeadingLevel=\"Level1\" Text=\"x\"/>"),
        ("HyperlinkButton", $"<HyperlinkButton {Ns} Content=\"a\" NavigateUri=\"mailto:a@b.c\"/>"),
        ("ThemeBrushes", $"<Border {Ns} Background=\"{{ThemeResource CardBackgroundFillColorDefaultBrush}}\" BorderBrush=\"{{ThemeResource CardStrokeColorDefaultBrush}}\"/>"),
        ("TextStyles", $"<TextBlock {Ns} Style=\"{{StaticResource TitleTextBlockStyle}}\"/>"),
        ("AccentButton", $"<Button {Ns} Style=\"{{StaticResource AccentButtonStyle}}\"/>"),
    ];

    public static void Run(string logPath)
    {
        var lines = new List<string>();
        foreach (var (name, xaml) in Pieces)
        {
            try
            {
                _ = XamlReader.Load(xaml);
                lines.Add($"OK   {name}");
            }
            catch (Exception ex)
            {
                lines.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllLines(logPath, lines);
    }
}
