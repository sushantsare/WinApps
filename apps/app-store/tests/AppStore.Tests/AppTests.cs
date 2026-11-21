using AppStore.Core.Models;
using AppStore.Core.Services;
using AppStore.Core.ViewModels;
using AppStore.Core.Winget;
using Xunit;

namespace AppStore.Tests;

/// <summary>Dialogs that always answer "yes" and remember what they were asked.</summary>
internal sealed class FakeDialogs : IDialogs
{
    public List<string> Asked { get; } = [];

    public List<string> Toasts { get; } = [];

    public bool Answer { get; set; } = true;

    public Task<bool> ConfirmAsync(string title, string body, string primary, IReadOnlyList<string>? warnings = null, string? info = null, bool destructive = false)
    {
        Asked.Add(title + (info is null ? string.Empty : " | " + info));
        return Task.FromResult(Answer);
    }

    public Task<PackageOptions?> EditOptionsAsync(PackageItem app, PackageOptions current, IReadOnlyList<string> versions) =>
        Task.FromResult<PackageOptions?>(new PackageOptions { SkipMajorUpdates = true });

    public Task<IReadOnlyList<AppListEntry>?> ChooseImportAsync(AppListFile file, ISet<string> alreadyInstalled) =>
        Task.FromResult<IReadOnlyList<AppListEntry>?>(file.Apps);

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension, string typeLabel) => Task.FromResult<string?>(null);

    public Task<string?> PickOpenFileAsync(string extension, string typeLabel) => Task.FromResult<string?>(null);

    public Task ShowMessageAsync(string title, string body) => Task.CompletedTask;

    public void Toast(string message, bool isError = false) => Toasts.Add(message);

    public void OpenUri(string uri)
    {
    }
}

public sealed class AppTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AppStoreTests-" + Guid.NewGuid().ToString("N"));

    public AppTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Root = _dir;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (AppServices S, FakeDialogs D) Make()
    {
        var d = new FakeDialogs();
        var s = new AppServices(new FakePackageManager(speedMs: 1), new SettingsStore(Path.Combine(_dir, "s.json")), new HistoryStore(Path.Combine(_dir, "h.json")), d, elevation: null);
        return (s, d);
    }

    [Fact]
    public async Task Updates_hide_ignored_and_hold_back_major()
    {
        var (s, _) = Make();
        s.SettingsStore.Update(x =>
        {
            x.Options["Python.Python.3.13"] = new PackageOptions { IgnoreUpdates = true };
            x.Options["OBSProject.OBSStudio"] = new PackageOptions { SkipMajorUpdates = true };
            x.SkippedVersions["7zip.7zip"] = "24.09";
        });
        var vm = new UpdatesViewModel(s);
        await vm.CheckAsync();

        Assert.Equal(PageState.Ready, vm.State);
        Assert.DoesNotContain(vm.Items, i => i.Id == "Python.Python.3.13");
        Assert.DoesNotContain(vm.Items, i => i.Id == "7zip.7zip");
        Assert.Single(vm.Held);
        Assert.Equal("OBSProject.OBSStudio", vm.Held[0].Id);
        Assert.Equal(4, vm.Items.Count);
        Assert.Equal(4, vm.CheckedCount);
    }

    [Fact]
    public async Task Update_all_runs_queue_and_writes_history()
    {
        var (s, d) = Make();
        var vm = new UpdatesViewModel(s);
        await vm.CheckAsync();
        var count = vm.CheckedCount;
        var done = new TaskCompletionSource();
        s.Queue.QueueEmptied += (_, _) => done.TrySetResult();

        await vm.UpdateAllAsync();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Contains(d.Asked, a => a.StartsWith("Update ", StringComparison.Ordinal));
        Assert.Equal(count, s.History.Entries.Count(e => e.Action == "Updated"));
        Assert.All(s.Queue.Finished, f => Assert.Equal(OpState.Done, f.State));
        Assert.Empty(s.Queue.Active);
    }

    [Fact]
    public async Task Failed_install_is_reported_and_logged()
    {
        var (s, _) = Make();
        var done = new TaskCompletionSource();
        s.Queue.QueueEmptied += (_, _) => done.TrySetResult();
        s.Queue.Enqueue([new OperationRequest { Kind = OpKind.Install, Id = "Microsoft.VisualStudioCode.Insiders", Name = "VS Code Insiders", Source = "winget" }], elevate: false);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var item = Assert.Single(s.Queue.Finished);
        Assert.Equal(OpState.Failed, item.State);
        Assert.Contains("0x8A150107", item.Message, StringComparison.Ordinal);
        Assert.Contains(s.History.Entries, e => e.Action == "Install failed");
    }

    [Fact]
    public void Admin_once_rule()
    {
        var (s, _) = Make();
        var one = new OperationRequest { Kind = OpKind.Update, Options = new PackageOptions() };
        Assert.False(s.ShouldElevate([one])); // no elevation service in tests
        Assert.True(BatchRunnerFacts.WorthRetrying(1603));
        Assert.False(BatchRunnerFacts.WorthRetrying(unchecked((int)0x8A15010A)));
    }

    [Fact]
    public async Task Search_marks_installed_apps()
    {
        var (s, _) = Make();
        s.InstalledIds.Add("VSCodium.VSCodium");
        var vm = new SearchViewModel(s) { Query = "code" };
        await vm.SearchAsync();
        Assert.Equal(PageState.Ready, vm.State);
        Assert.True(vm.Results.First(r => r.Id == "VSCodium.VSCodium").IsInstalled);
        Assert.Contains("code", s.Settings.RecentSearches);

        vm.Query = "zzzz-nothing";
        await vm.SearchAsync();
        Assert.Equal(PageState.Empty, vm.State);
    }

    [Fact]
    public async Task Installed_filter_by_source_and_text()
    {
        var (s, _) = Make();
        var vm = new InstalledViewModel(s);
        await vm.LoadAsync();
        var all = vm.Visible.Count;
        vm.SourceFilter = 2;
        Assert.Single(vm.Visible);
        Assert.Equal("HP Smart", vm.Visible[0].Name);
        vm.SourceFilter = 0;
        vm.Filter = "vlc";
        Assert.Single(vm.Visible);
        vm.Filter = string.Empty;
        Assert.Equal(all, vm.Visible.Count);
    }

    [Fact]
    public void Settings_round_trip_and_damaged_file()
    {
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        store.Update(s =>
        {
            s.Options["Google.Chrome"] = new PackageOptions { Scope = "machine" };
            s.Theme = "dark";
        });
        var again = new SettingsStore(file);
        Assert.Equal("machine", again.Current.OptionsFor("google.chrome").Scope);
        Assert.Equal("dark", again.Current.Theme);

        File.WriteAllText(file, "{ not json");
        var broken = new SettingsStore(file);
        Assert.Equal("system", broken.Current.Theme);
        Assert.True(File.Exists(file + ".broken"));
    }

    [Fact]
    public void App_list_round_trip()
    {
        var settings = new AppSettings();
        settings.Options["VideoLAN.VLC"] = new PackageOptions { SkipMajorUpdates = true };
        var json = AppListIo.Serialize(
            [new PackageItem("VideoLAN.VLC", "VLC media player") { Source = "winget", Version = "3.0.22" }, new PackageItem(@"ARP\X", "Not winget")],
            settings);
        var file = AppListIo.Parse(json);
        var e = Assert.Single(file.Apps);
        Assert.Equal("VideoLAN.VLC", e.Id);
        Assert.True(e.Options!.SkipMajorUpdates);
        Assert.Throws<InvalidDataException>(() => AppListIo.Parse("{}"));
        Assert.Throws<InvalidDataException>(() => AppListIo.Parse("hello"));
    }

    [Fact]
    public void History_prune_and_csv()
    {
        var h = new HistoryStore(Path.Combine(_dir, "hist.json"));
        h.Add(new HistoryEntry { Time = DateTimeOffset.Now.AddDays(-400), Name = "Old", Action = "Updated" });
        h.Add(new HistoryEntry { Time = DateTimeOffset.Now, Name = "New \"quoted\"", Action = "Installed" });
        h.Prune(365);
        Assert.Single(h.Entries);
        Assert.Contains("\"New \"\"quoted\"\"\"", h.ToCsv(), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_tags()
    {
        Assert.Equal("Ignored", AppServices.TagFor(new PackageOptions { IgnoreUpdates = true, SkipMajorUpdates = true }));
        Assert.Equal("Skip major updates", AppServices.TagFor(new PackageOptions { SkipMajorUpdates = true }));
        Assert.Equal(string.Empty, AppServices.TagFor(new PackageOptions()));
        Assert.Equal("Install options saved", AppServices.TagFor(new PackageOptions { Scope = "user" }));
    }

    [Fact]
    public void Elevated_events_round_trip()
    {
        var folder = ElevatedHelper.WriteRequests([new OperationRequest { Id = "A", Name = "A" }]);
        File.WriteAllText(Path.Combine(folder, ElevatedHelper.CancelFile), "0\n-1\n");
        var c = ElevatedHelper.CancelledIndexes(folder);
        Assert.Contains(0, c);
        Assert.Contains(-1, c);
        Assert.Equal(OpState.Done, ElevatedHelper.ParseEvent("{\"index\":0,\"state\":\"Done\"}")!.State);
        Assert.Null(ElevatedHelper.ParseEvent("garbage"));
    }
}

internal static class BatchRunnerFacts
{
    public static bool WorthRetrying(int code) => BatchRunner.IsWorthRetrying(code);
}
