using AppStore.Core.Services;
using AppStore.Core.ViewModels;
using AppStore.Core.Winget;
using AppStore.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AppStore;

public partial class App : Application
{
    private readonly string[] _args;

    public App(string[] args)
    {
        _args = args;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true;
            Window?.ShowCrashNotice();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };
    }

    public static MainWindow? Window { get; private set; }

    public static AppServices Services { get; private set; } = null!;

    public static UpdatesViewModel Updates { get; private set; } = null!;

    public static SearchViewModel Search { get; private set; } = null!;

    public static InstalledViewModel Installed { get; private set; } = null!;

    public static QueueViewModel QueueVm { get; private set; } = null!;

    public static HistoryViewModel History { get; private set; } = null!;

    public static SettingsViewModel SettingsVm { get; private set; } = null!;

    /// <summary>True when launched with --test-data (sample data, nothing touches the PC).</summary>
    public static bool TestMode { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Ui.Post = a =>
        {
            if (dispatcher.HasThreadAccess)
            {
                a();
            }
            else
            {
                dispatcher.TryEnqueue(() => a());
            }
        };

        TestMode = _args.Contains("--test-data");
        if (TestMode)
        {
            // Test runs keep their own settings and history, away from the real ones.
            AppPaths.Root = Path.Combine(Path.GetTempPath(), "AppStoreTest-" + Environment.ProcessId);
        }

        var settings = new SettingsStore();
        var history = new HistoryStore();
        history.Prune(settings.Current.HistoryDays);
        if (TestMode)
        {
            SeedTestData(settings, history);
        }

        IPackageManager pm = TestMode
            ? new FakePackageManager { WingetBroken = _args.Contains("--winget-broken") }
            : new WingetPackageManager();
        var dialogs = new DialogService();
        var elevation = TestMode ? null : new ElevationService();
        Services = new AppServices(pm, settings, history, dialogs, elevation);
        Updates = new UpdatesViewModel(Services);
        Search = new SearchViewModel(Services);
        Installed = new InstalledViewModel(Services);
        QueueVm = new QueueViewModel(Services);
        History = new HistoryViewModel(Services);
        SettingsVm = new SettingsViewModel(Services)
        {
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        };

        Window = new MainWindow(_args);
        dialogs.Attach(Window);
        Window.Activate();
        _ = Task.Run(ElevatedHelper.CleanOld);
    }

    private static void SeedTestData(SettingsStore settings, HistoryStore history)
    {
        settings.Update(s =>
        {
            s.Options["OBSProject.OBSStudio"] = new Core.Models.PackageOptions { SkipMajorUpdates = true };
            s.Options["Python.Python.3.13"] = new Core.Models.PackageOptions { IgnoreUpdates = true };
            s.Options["Google.Chrome"] = new Core.Models.PackageOptions { Scope = "machine", Architecture = "x64", CloseBeforeUpdate = true };
            s.SkippedVersions["Zoom.Zoom"] = "6.3.0";
            s.RecentSearches = ["vlc", "obs studio", "python", "telegram"];
        });
        var now = DateTimeOffset.Now;
        history.Add(new Core.Models.HistoryEntry { Time = now.AddMinutes(-20), Id = "7zip.7zip", Name = "7-Zip", Action = "Updated", Detail = "24.07 → 24.08", Kind = Core.Models.HistoryKind.Success });
        history.Add(new Core.Models.HistoryEntry { Time = now.AddMinutes(-21), Id = "Microsoft.VisualStudioCode", Name = "Visual Studio Code", Action = "Install failed", Detail = "The installer reported an error. (code 0x8A150107)", Kind = Core.Models.HistoryKind.Failure });
        history.Add(new Core.Models.HistoryEntry { Time = now.AddHours(-3), Id = "Telegram.TelegramDesktop", Name = "Telegram Desktop", Action = "Installed", Detail = "5.9.0", Kind = Core.Models.HistoryKind.Success });
        history.Add(new Core.Models.HistoryEntry { Time = now.AddDays(-1), Id = "Google.Chrome", Name = "Google Chrome", Action = "Updated", Detail = "130.0 → 131.0.6778", Kind = Core.Models.HistoryKind.Success });
        history.Add(new Core.Models.HistoryEntry { Time = now.AddDays(-1).AddHours(-2), Id = "Python.Python.3.13", Name = "Python 3.13", Action = "Ignored", Detail = "All updates", Kind = Core.Models.HistoryKind.Info });
    }
}

internal static class CrashLog
{
    public static string Folder => AppPaths.Logs;

    public static void Write(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "crash.log"),
                $"{DateTimeOffset.Now:O} | v{typeof(CrashLog).Assembly.GetName().Version} | {Environment.OSVersion}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Never crash while logging a crash.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
