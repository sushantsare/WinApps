using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;

namespace PipelineCheck.UITests;

/// <summary>Launches the published app like a user and walks every page, saving screenshots.</summary>
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

    private static AutomationElement WaitFor(Window win, string automationId) =>
        Retry.WhileNull(() => win.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true, timeoutMessage: $"'{automationId}' not found").Result!;

    /// <summary>Selects/invokes through UI Automation (works even if the element is off-screen), mouse click as last resort.</summary>
    private static void Activate(AutomationElement el)
    {
        if (el.Patterns.SelectionItem.TryGetPattern(out var sel)) { sel.Select(); return; }
        if (el.Patterns.Invoke.TryGetPattern(out var inv)) { inv.Invoke(); return; }
        el.Click();
    }

    /// <summary>Runner screens are small (1024×768): maximize so the whole window is visible.</summary>
    private static void Maximize(Window win)
    {
        if (win.Patterns.Window.TryGetPattern(out var w) && w.CanMaximize.ValueOrDefault)
        {
            w.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized);
            Thread.Sleep(600);
        }
    }

    private static void Shot(Window win, string name)
    {
        Thread.Sleep(900); // let transitions settle
        Capture.Element(win).ToFile(Path.Combine(Shots, name + ".png"));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Walkthrough_every_page(string theme)
    {
        using var app = Application.Launch(new ProcessStartInfo(Exe, $"--theme {theme}"));
        try
        {
            using var automation = new UIA3Automation();
            var win = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
            Assert.NotNull(win);
            win.SetForeground();
            Maximize(win);

            WaitFor(win, "HomeTitle");
            Shot(win, $"{theme}-01-home");

            WaitFor(win, "ClickButton").AsButton().Invoke();
            Retry.WhileFalse(() => WaitFor(win, "ClickCount").Name.Contains('1', StringComparison.Ordinal),
                TimeSpan.FromSeconds(5), throwOnTimeout: true);

            Activate(WaitFor(win, "NavAbout"));
            var email = WaitFor(win, "EmailLink");
            Assert.Equal("sushant.sare@gmail.com", email.Name);
            Shot(win, $"{theme}-02-about");

            Activate(WaitFor(win, "NavSettings"));
            WaitFor(win, "SettingsTitle");
            Shot(win, $"{theme}-03-settings");

            // Theme switch from Settings must apply live.
            Activate(WaitFor(win, theme == "dark" ? "ThemeLight" : "ThemeDark"));
            Shot(win, $"{theme}-04-settings-switched");

            Assert.False(app.HasExited, "App exited during the walkthrough");
        }
        finally
        {
            app.Close();
        }
    }

    [Fact]
    public void Keyboard_tab_moves_focus_through_controls()
    {
        using var app = Application.Launch(Exe);
        try
        {
            using var automation = new UIA3Automation();
            var win = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
            win.SetForeground();
            WaitFor(win, "HomeTitle");

            var visited = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                Keyboard.Press(VirtualKeyShort.TAB);
                Thread.Sleep(150);
                var focused = automation.FocusedElement();
                visited.Add($"{focused.Properties.AutomationId.ValueOrDefault}|{focused.Properties.Name.ValueOrDefault}");
            }

            File.WriteAllLines(Path.Combine(Shots, "..", "keyboard-tab-order.txt"), visited);
            Assert.Contains(visited, v => v.StartsWith("ClickButton", StringComparison.Ordinal));
            Assert.True(visited.Distinct().Count() >= 3, "Tab should visit several controls");
        }
        finally
        {
            app.Close();
        }
    }
}
