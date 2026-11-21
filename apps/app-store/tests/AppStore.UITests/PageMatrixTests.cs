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
    internal static readonly string[] Pages = ["NavSearch", "NavUpdates", "NavInstalled", "NavQueue", "NavHistory", "NavSettings"];

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
            throwOnTimeout: true, ignoreException: true, timeoutMessage: $"'{id}' not found").Result!;

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
            // Sidebar order as agreed: Search, Updates, Installed, Queue, History (Settings sits at the bottom).
            var top = items.Where(i => i != "NavSettings").ToList();
            Assert.Equal(["NavSearch", "NavUpdates", "NavInstalled", "NavQueue", "NavHistory"], top);
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
        var problems = new List<string>();
        var narrow = size.StartsWith("620", StringComparison.Ordinal);
        if (narrow)
        {
            // Narrow windows hide the menu behind ☰: open each page directly with --page.
            foreach (var page in Pages)
            {
                var tag = page[3..].ToLowerInvariant();
                var app = Application.Launch(new ProcessStartInfo(Exe, $"--test-data --size {size} --theme {theme} --page {tag}"));
                using var ui = new UIA3Automation();
                try
                {
                    var win = app.GetMainWindow(ui, TimeSpan.FromSeconds(30));
                    win.SetForeground();
                    WaitFor(win, "Nav", 30);
                    Thread.Sleep(1500);
                    var name = $"{tag}-{size}-closed-{theme}";
                    Capture.Element(win).ToFile(Path.Combine(Shots, name + ".png"));
                    problems.AddRange(Overflow(win, name));
                }
                finally
                {
                    app.Close();
                }
            }
        }
        else
        {
            var app = Application.Launch(new ProcessStartInfo(Exe, $"--test-data --size {size} --theme {theme}"));
            using var ui = new UIA3Automation();
            try
            {
                var win = app.GetMainWindow(ui, TimeSpan.FromSeconds(30));
                win.SetForeground();
                WaitFor(win, "UpdatesList", 30);
                foreach (var page in Pages)
                {
                    foreach (var sidebar in new[] { "open", "closed" })
                    {
                        SetSidebar(win, open: sidebar == "open");
                        GoTo(win, page);
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
        }

        Assert.True(problems.Count == 0, "Content outside the window:\n" + string.Join("\n", problems.Take(40)));
    }

    private static void GoTo(Window win, string page)
    {
        var item = WaitFor(win, page);
        if (item.Patterns.SelectionItem.TryGetPattern(out var sel))
        {
            sel.Select();
        }
        else
        {
            item.Click();
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

    private static readonly Dictionary<string, string> Titles = new()
    {
        ["updates"] = "UpdatesTitle",
        ["search"] = "SearchTitle",
        ["installed"] = "InstalledTitle",
        ["queue"] = "QueueTitle",
        ["history"] = "HistoryTitle",
        ["settings"] = "SettingsTitle",
    };

    /// <summary>
    /// The page must start right next to the sidebar and nothing may stick out past the window's right edge.
    /// Checks the whole window (not just one container) and fails if it found too few elements to be meaningful.
    /// </summary>
    private static IEnumerable<string> Overflow(Window win, string setup)
    {
        var problems = new List<string>();
        var w = win.BoundingRectangle;
        var titleBarBottom = w.Top + 40;
        var page = setup.Split('-')[0];
        var title = win.FindFirstDescendant(cf => cf.ByAutomationId(Titles[page]));
        if (title is null)
        {
            problems.Add($"{setup}: page title '{Titles[page]}' not found");
            return problems;
        }

        // Where the page area starts: right edge of the sidebar's Settings item (or the window edge when the menu is hidden).
        var navItem = win.FindFirstDescendant(cf => cf.ByAutomationId("NavSettings"));
        var paneRight = navItem is { IsOffscreen: false } && navItem.BoundingRectangle.Width > 0 ? navItem.BoundingRectangle.Right : w.Left;
        var scale = Math.Max(1.0, title.BoundingRectangle.Height / 36.0);
        var allowedGap = (int)(80 * scale);
        var gap = title.BoundingRectangle.Left - paneRight;
        if (gap > allowedGap || gap < 0)
        {
            problems.Add($"{setup}: page content starts {gap}px from the sidebar (allowed 0–{allowedGap}) — page is shifted");
        }

        var checkedCount = 0;
        foreach (var el in win.FindAllDescendants())
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

            if (r.Width <= 0 || r.Height <= 0 || r.Top >= w.Bottom || r.Top < titleBarBottom)
            {
                continue;
            }

            checkedCount++;
            if (r.Right > w.Right + 2)
            {
                var id = el.Properties.AutomationId.ValueOrDefault;
                var n = el.Properties.Name.ValueOrDefault;
                problems.Add($"{setup}: '{(string.IsNullOrEmpty(id) ? n : id)}' ({el.ControlType}) right edge {r.Right} > window {w.Right}");
            }
        }

        if (checkedCount < 4)
        {
            problems.Add($"{setup}: only {checkedCount} elements checked — the check isn't seeing the page");
        }

        return problems;
    }
}
