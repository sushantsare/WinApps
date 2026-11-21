using Microsoft.UI.Xaml;

namespace PipelineCheck;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => CrashLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow(Environment.GetCommandLineArgs());
        Window.Activate();
    }
}

internal static class CrashLog
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sushant", "PipelineCheck", "Logs");

    public static void Write(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "crash.log"),
                $"{DateTimeOffset.Now:O} | v{typeof(App).Assembly.GetName().Version} | {Environment.OSVersion}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Never crash while logging a crash.
        }
    }
}
