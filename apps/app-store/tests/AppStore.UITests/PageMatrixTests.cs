using System.Diagnostics;
using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace AppStore.UITests;

/// <summary>
/// Every page, every setup: 3 window sizes × sidebar open/collapsed × light/dark. On each one the page must fit
/// inside the window (nothing past the right edge, no sideways scrolling) and gets a screenshot.
/// </summary>
public class PageMatrixTests
{
    /// <summary>Every page in the sidebar. A page added to the app without being added here fails PageInventory.</summary>
    internal static readonly string[] Pages = ["NavUpdates", "NavSearch", "NavInstalled", "NavQueue", "NavHistory", "NavSettings"];

    private static string Exe => Environment.GetEnvironmentVariable("APP_EXE") ?? throw new InvalidOperationException("APP_EXE not set");

    private static string Shots
    {
        get
        {
            var dir = Path.Combine(Environment.GetEnvironmentVariable("SCREENS_DIR") ?? Path.GetTempPath(), "matrix");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static TheoryData<string, string> Setups() => new()
    {
        { "620x700", "light" }, { "620x700", "dark" },
        { "1100x700", "light" }, { "1100x700", "dark" },
        { "1920x1040", "light" }, { "1920x1040", "dark" },
    };

    private static AutomationElement WaitFor(AutomationElement root, string id, int seconds = 20) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByAutomationId(id)), TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true, timeoutMessage: $"'{id}' not found").Result!;

    [Fact]
    public void Page_inventory_matches_the_sidebar()
    {
        var app = Application.Launch(new ProcessStartInfo(Exe, "--test-data --size 1100x700"));
        using var ui = new UIA3Automation();
        try
        {
            var win = app.GetMainWindow(ui, TimeSpan.FromSeconds(30));
            var nav = WaitFor(win, "Nav");
            var items = nav.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
                .Select(i => i.Properties.AutomationId.ValueOrDefault)
                .Where(id => id is { Length: > 0 } && id.StartsWith("Nav", StringComparison.Ordinal))
                .Distinct()
                .ToList();
            var missing = items.Except(Pages).ToList();
            Assert.True(missing.Count == 0, "Pages without tests: " + string.Join(", ", missing));
            Assert.All(Pages, p => Assert.Contains(p, items));
        }
        finally
        {
            app.Close();
        }
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public void Every_page_fits_with_sidebar_open_and_closed(string size, string theme)
    {
        var app = Application.Launch(new ProcessStartInfo(Exe, $"--test-data --size {size} --theme {theme}"));
        using var ui = new UIA3Automation();
        var problems = new List<string>();
        try
        {
            var win = app.GetMainWindow(ui, TimeSpan.FromSeconds(30));
            win.SetForeground();
            WaitFor(win, "UpdatesList", 30);
            var narrow = size.StartsWith("620", StringComparison.Ordinal);

            foreach (var page in Pages)
            {
                foreach (var sidebar in narrow ? new[] { "closed" } : ["open", "closed"])
                {
                    if (!narrow)
                    {
                        SetSidebar(win, open: sidebar == "open");
                    }

                    GoTo(win, page, narrow);
                    Thread.Sleep(900);
                    var name = $"{page[3..].ToLowerInvariant()}-{size}-{sidebar}-{theme}";
                    Capture.Element(win).ToFile(Path.Combine(Shots, name + ".png"));
                    problems.AddRange(Overflow(win, name));
                }
            }
        }
        finally
        {
            app.Close();
        }

        Assert.True(problems.Count == 0, "Content outside the window:\n" + string.Join("\n", problems.Take(40)));
    }

    private static void GoTo(Window win, string page, bool narrow)
    {
        if (narrow)
        {
            // Narrow: menu is behind the ☰ in the title bar.
            var toggle = win.FindFirstDescendant(cf => cf.ByAutomationId("PaneToggleButton")) ?? win.FindFirstDescendant(cf => cf.ByAutomationId("TogglePaneButton"));
            toggle?.Click();
            Thread.Sleep(400);
        }

        var item = WaitFor(win, page);
        if (item.Patterns.SelectionItem.TryGetPattern(out var sel))
        {
            sel.Select();
        }
        else
        {
            item.Click();
        }

        if (narrow)
        {
            Thread.Sleep(300);
            // Close the overlay menu if it is still open.
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        }
    }

    private static void SetSidebar(Window win, bool open)
    {
        var nav = WaitFor(win, "Nav");
        var settings = WaitFor(win, "NavSettings");
        var isOpen = settings.BoundingRectangle.Width > 120;
        if (isOpen != open)
        {
            var toggle = nav.FindFirstDescendant(cf => cf.ByAutomationId("TogglePaneButton"));
            toggle?.Click();
            Thread.Sleep(600);
        }
    }

    /// <summary>Anything inside the page that is visible top-to-bottom but sticks out past the window's right edge.</summary>
    private static IEnumerable<string> Overflow(Window win, string setup)
    {
        var frame = win.FindFirstDescendant(cf => cf.ByAutomationId("ContentFrame"));
        if (frame is null)
        {
            yield return setup + ": page frame not found";
            yield break;
        }

        var w = win.BoundingRectangle;
        var f = frame.BoundingRectangle;
        if (f.Right > w.Right + 2)
        {
            yield return $"{setup}: page area ends at {f.Right}, window ends at {w.Right}";
        }

        foreach (var el in frame.FindAllDescendants())
        {
            Rectangle r;
            try
            {
                r = el.BoundingRectangle;
            }
            catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
            {
                continue;
            }

            if (r.Width <= 0 || r.Height <= 0 || r.Top >= w.Bottom || r.Bottom <= f.Top)
            {
                continue; // empty, or scrolled out of view vertically
            }

            if (r.Right > w.Right + 2)
            {
                var id = el.Properties.AutomationId.ValueOrDefault;
                var n = el.Properties.Name.ValueOrDefault;
                yield return $"{setup}: '{(string.IsNullOrEmpty(id) ? n : id)}' ({el.ControlType}) right edge {r.Right} > window {w.Right}";
            }
        }
    }
}
