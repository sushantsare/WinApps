using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;

namespace AppStore.UITests;

/// <summary>Launches the published app with sample data (--test-data) and walks every page like a user.</summary>
public class WalkthroughTests
{
    private static string Exe => Environment.GetEnvironmentVariable("APP_EXE")
        ?? throw new InvalidOperationException("APP_EXE not set");

    private static string Shots
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("SCREENS_DIR") ?? Path.Combine(Path.GetTempPath(), "screens");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static AutomationElement WaitFor(AutomationElement root, string automationId, int seconds = 20) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true, timeoutMessage: $"'{automationId}' not found").Result!;

    private static AutomationElement WaitForName(AutomationElement root, string name, int seconds = 20) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByName(name)),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true, timeoutMessage: $"'{name}' not found").Result!;

    private static void Activate(AutomationElement el)
    {
        if (el.Patterns.SelectionItem.TryGetPattern(out var sel))
        {
            sel.Select();
            return;
        }

        if (el.Patterns.Invoke.TryGetPattern(out var inv))
        {
            inv.Invoke();
            return;
        }

        if (el.Patterns.Toggle.TryGetPattern(out var tog))
        {
            tog.Toggle();
            return;
        }

        el.Click();
    }

    private static void Maximize(Window win)
    {
        if (win.Patterns.Window.TryGetPattern(out var w) && w.CanMaximize.ValueOrDefault)
        {
            w.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized);
            Thread.Sleep(700);
        }
    }

    private static void Shot(Window win, string name)
    {
        Thread.Sleep(900);
        Capture.Element(win).ToFile(Path.Combine(Shots, name + ".png"));
    }

    /// <summary>Clicks the primary button of the open ContentDialog.</summary>
    private static void ConfirmDialog(Window win, string primaryName)
    {
        var btn = Retry.WhileNull(() => win.FindFirstDescendant(cf => cf.ByName(primaryName).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))),
            TimeSpan.FromSeconds(10), throwOnTimeout: true, timeoutMessage: $"dialog button '{primaryName}' not found").Result!;
        Activate(btn);
        Thread.Sleep(500);
    }

    private static (Application App, UIA3Automation Ui, Window Win) Launch(string extraArgs)
    {
        var app = Application.Launch(new ProcessStartInfo(Exe, "--test-data " + extraArgs));
        var ui = new UIA3Automation();
        var win = app.GetMainWindow(ui, TimeSpan.FromSeconds(30));
        Assert.NotNull(win);
        win.SetForeground();
        return (app, ui, win);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Walkthrough_every_page(string theme)
    {
        var (app, ui, win) = Launch("--theme " + theme);
        try
        {
            Maximize(win);
            var list = WaitFor(win, "UpdatesList");
            Retry.WhileFalse(() => list.FindAllChildren().Length >= 3, TimeSpan.FromSeconds(15), throwOnTimeout: true, timeoutMessage: "updates never loaded");
            WaitForName(win, "Install anyway: OBS Studio");
            Shot(win, $"{theme}-01-updates");

            // Install options from the details panel
            Activate(WaitFor(win, "UpdateOptions"));
            WaitFor(win, "OptSkipMajor");
            Shot(win, $"{theme}-02-install-options");
            ConfirmDialog(win, "Cancel");

            // Update all → confirm → queue
            Activate(WaitFor(win, "UpdateAll"));
            WaitForName(win, "Cancel");
            Shot(win, $"{theme}-03-update-confirm");
            ConfirmDialog(win, "Update 5 apps");
            Activate(WaitFor(win, "NavQueue"));
            WaitFor(win, "QueueTitle");
            Shot(win, $"{theme}-04-queue-running");
            Retry.WhileFalse(() => win.FindFirstDescendant(cf => cf.ByAutomationId("QueueActive")) is null || win.FindFirstDescendant(cf => cf.ByAutomationId("QueueActive")).IsOffscreen,
                TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500));
            Shot(win, $"{theme}-05-queue-done");

            // Search: type, enter, tick two, see selection bar
            Activate(WaitFor(win, "NavSearch"));
            var box = WaitFor(win, "SearchBox");
            box.Focus();
            var edit = box.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)) ?? box;
            edit.AsTextBox().Text = "code";
            Keyboard.Press(VirtualKeyShort.ENTER);
            var results = WaitFor(win, "ResultsList");
            Retry.WhileFalse(() => results.FindAllChildren().Length >= 3, TimeSpan.FromSeconds(15), throwOnTimeout: true, timeoutMessage: "no search results");
            foreach (var name in new[] { "Select Visual Studio Code", "Select VSCodium" })
            {
                Activate(WaitForName(win, name));
            }

            WaitFor(win, "SelectionBar");
            Shot(win, $"{theme}-06-search");

            Activate(WaitFor(win, "NavInstalled"));
            var inst = WaitFor(win, "InstalledList");
            Retry.WhileFalse(() => inst.FindAllChildren().Length >= 3, TimeSpan.FromSeconds(15), throwOnTimeout: true, timeoutMessage: "installed never loaded");
            Shot(win, $"{theme}-07-installed");

            Activate(WaitFor(win, "NavHistory"));
            WaitFor(win, "HistoryTitle");
            Shot(win, $"{theme}-08-history");

            Activate(WaitFor(win, "NavSettings"));
            WaitFor(win, "SettingsTitle");
            Assert.Equal("sushant.sare@gmail.com", WaitFor(win, "EmailLink").Name);
            Shot(win, $"{theme}-09-settings");

            // Collapse the sidebar with the ☰ button, then open it again
            var toggle = WaitFor(win, "TogglePaneButton");
            Activate(toggle);
            Activate(WaitFor(win, "NavUpdates"));
            Shot(win, $"{theme}-10-sidebar-collapsed");
            Activate(toggle);

            Assert.False(app.HasExited, "App exited during the walkthrough");
        }
        finally
        {
            app.Close();
            ui.Dispose();
        }
    }

    [Fact]
    public void Narrow_window_and_winget_error()
    {
        var (app, ui, win) = Launch("--winget-broken --size 620x700");
        try
        {
            WaitFor(win, "WingetError");
            WaitFor(win, "FixWinget");
            Shot(win, "narrow-01-winget-error");
            Activate(WaitFor(win, "FixWinget"));
            var list = WaitFor(win, "UpdatesList", 30);
            Retry.WhileFalse(() => list.FindAllChildren().Length >= 3, TimeSpan.FromSeconds(20), throwOnTimeout: true, timeoutMessage: "updates never loaded after fix");
            Shot(win, "narrow-02-updates");
            Assert.False(app.HasExited);
        }
        finally
        {
            app.Close();
            ui.Dispose();
        }
    }

    [Fact]
    public void Keyboard_tab_moves_focus_through_controls()
    {
        var (app, ui, win) = Launch(string.Empty);
        try
        {
            WaitFor(win, "UpdatesList");
            var visited = new List<string>();
            for (var i = 0; i < 16; i++)
            {
                Keyboard.Press(VirtualKeyShort.TAB);
                Thread.Sleep(150);
                var focused = ui.FocusedElement();
                visited.Add($"{focused.Properties.AutomationId.ValueOrDefault}|{focused.Properties.Name.ValueOrDefault}");
            }

            File.WriteAllLines(Path.Combine(Shots, "..", "keyboard-tab-order.txt"), visited);
            Assert.True(visited.Distinct().Count() >= 4, "Tab should visit several controls");
        }
        finally
        {
            app.Close();
            ui.Dispose();
        }
    }
}
