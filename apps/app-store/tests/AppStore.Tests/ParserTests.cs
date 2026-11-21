using AppStore.Core;
using AppStore.Core.Models;
using AppStore.Core.Winget;
using Xunit;

namespace AppStore.Tests;

public class ParserTests
{
    private const string Upgrade = """
        \r-\r\\\r|\r
        Name                         Id                       Version      Available    Source
        -----------------------------------------------------------------------------------------
        Google Chrome                Google.Chrome            131.0.6778   132.0.6834   winget
        VLC media player             VideoLAN.VLC             3.0.21       3.0.22       winget
        Microsoft PowerToys (Preview) Microsoft.PowerToys     0.86.0       0.87.1       winget
        3 upgrades available.
        """;

    [Fact]
    public void Upgrade_table_is_parsed()
    {
        var list = WingetParsers.ParseUpgradeTable(Upgrade.Replace("\\r", "\r", StringComparison.Ordinal));
        Assert.Equal(3, list.Count);
        Assert.Equal("Google.Chrome", list[0].Id);
        Assert.Equal("Google Chrome", list[0].Name);
        Assert.Equal("131.0.6778", list[0].Version);
        Assert.Equal("132.0.6834", list[0].Available);
        Assert.Equal("winget", list[0].Source);
        Assert.Equal("VideoLAN.VLC", list[1].Id);
    }

    [Fact]
    public void Wide_characters_keep_columns_aligned()
    {
        const string table = """
            Name       Id              Version
            ---------------------------------
            微信       Tencent.WeChat  3.9.12
            """;
        // Header columns: Name@0, Id@11, Version@27; the two wide chars take 4 console columns.
        var rows = WingetParsers.ParseTables(table);
        Assert.Single(rows);
        Assert.Equal("微信", rows[0][0]["Name"]);
        Assert.Equal("Tencent.WeChat", rows[0][0]["Id"]);
        Assert.Equal("3.9.12", rows[0][0]["Version"]);
    }

    [Fact]
    public void Search_table_and_empty_output()
    {
        const string search = """
            Name               Id                         Version  Match        Source
            ------------------------------------------------------------------------------
            Visual Studio Code Microsoft.VisualStudioCode 1.96.2   Moniker: vscode winget
            VSCodium           VSCodium.VSCodium          1.96.2               winget
            """;
        var list = WingetParsers.ParseSearchTable(search);
        Assert.Equal(2, list.Count);
        Assert.Equal("Microsoft.VisualStudioCode", list[0].Id);
        Assert.Empty(WingetParsers.ParseSearchTable("No package found matching input criteria."));
    }

    [Fact]
    public void Show_output_is_parsed()
    {
        const string show = """
            Found Google Chrome [Google.Chrome]
            Version: 132.0.6834.83
            Publisher: Google LLC
            Publisher Url: https://www.google.com/
            Description: Fast, secure browser.
            Homepage: https://www.google.com/chrome
            License: Freeware
            Release Notes:
              Security fixes.
              Faster tab restore.
            Release Notes Url: https://chromereleases.googleblog.com/
            Tags:
              browser
              chromium
            Installer:
              Installer Type: wix
              Installer Scope: machine
              Installer Url: https://dl.google.com/chrome/install/googlechromestandaloneenterprise64.msi
              Installer SHA256: 0123
            """;
        var d = WingetParsers.ParseShow(show);
        Assert.Equal("Google.Chrome", d.Id);
        Assert.Equal("Google Chrome", d.Name);
        Assert.Equal("Google LLC", d.Publisher);
        Assert.Equal("Security fixes.\nFaster tab restore.", d.ReleaseNotes);
        Assert.Equal("wix", d.InstallerType);
        Assert.Equal("machine", d.InstallerScope);
        Assert.StartsWith("https://dl.google.com/", d.InstallerUrl, StringComparison.Ordinal);
        Assert.Equal("https://chromereleases.googleblog.com/", d.ReleaseNotesUrl);
    }

    [Fact]
    public void Versions_are_parsed()
    {
        const string text = """
            Found VLC media player [VideoLAN.VLC]
            Version
            -------
            3.0.22
            3.0.21
            3.0.20
            """;
        Assert.Equal(["3.0.22", "3.0.21", "3.0.20"], WingetParsers.ParseVersions(text));
    }

    [Theory]
    [InlineData("  ██████████▒▒▒▒▒▒▒▒▒▒  12.0 MB / 40.0 MB", 30)]
    [InlineData("  ████████████████████  100%", 100)]
    public void Progress_lines_give_percent(string line, double expected)
    {
        var p = WingetParsers.ParseProgress(line);
        Assert.NotNull(p);
        Assert.Equal(expected, p.Percent, 0);
    }

    [Fact]
    public void Install_phase_is_detected()
    {
        Assert.Equal("Installing…", WingetParsers.ParseProgress("Starting package install...")!.Phase);
        Assert.Equal("Uninstalling…", WingetParsers.ParseProgress("Starting package uninstall...")!.Phase);
        Assert.Null(WingetParsers.ParseProgress("   "));
    }
}

public class VersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("131.0.6778", "132.0.6834", -1)]
    [InlineData("3.0.21", "3.0.21", 0)]
    [InlineData("24.09", "24.08", 1)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("2.0", "2.0.0", 0)]
    public void Compare_works(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(VersionCompare.Compare(a, b)));

    [Theory]
    [InlineData("30.2.3", "31.0.0", true)]
    [InlineData("3.0.21", "3.0.22", false)]
    [InlineData("", "2.0", false)]
    public void Major_jump(string from, string to, bool expected) => Assert.Equal(expected, VersionCompare.IsMajorJump(from, to));
}

public class ArgsTests
{
    [Fact]
    public void Update_args_include_options()
    {
        var args = WingetPackageManager.BuildArgs(new OperationRequest
        {
            Kind = OpKind.Update,
            Id = "Google.Chrome",
            Source = "winget",
            Version = "132.0",
            Options = new PackageOptions { Scope = "machine", Architecture = "x64", CustomArgs = "/norestart", Location = @"D:\Apps\Chrome" },
        });
        Assert.Equal(["upgrade", "--id", "Google.Chrome", "--exact", "--source", "winget", "--silent", "--accept-package-agreements",
            "--version", "132.0", "--scope", "machine", "--architecture", "x64", "--location", @"D:\Apps\Chrome", "--custom", "/norestart",
            "--include-unknown", "--accept-source-agreements", "--disable-interactivity"], args);
    }

    [Fact]
    public void Uninstall_of_non_winget_app_has_no_source()
    {
        var args = WingetPackageManager.BuildArgs(new OperationRequest { Kind = OpKind.Uninstall, Id = @"ARP\Machine\X64\HPSmart", Interactive = true });
        Assert.DoesNotContain("--source", args);
        Assert.Contains("--interactive", args);
        Assert.DoesNotContain("--accept-package-agreements", args);
    }
}
