using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

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

            WaitFor(win, "HomeTitle");
            Shot(win, $"{theme}-01-home");

            WaitFor(win, "ClickButton").AsButton().Invoke();
            Retry.WhileFalse(() => WaitFor(win, "ClickCount").Name.Contains('1', StringComparison.Ordinal),
                TimeSpan.FromSeconds(5), throwOnTimeout: true);

            WaitFor(win, "NavAbout").Click();
            var email = WaitFor(win, "EmailLink");
            Assert.Equal("sushant.sare@gmail.com", email.Name);
            Shot(win, $"{theme}-02-about");

            WaitFor(win, "NavSettings").Click();
            WaitFor(win, "SettingsTitle");
            Shot(win, $"{theme}-03-settings");

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
