using System.Diagnostics;
using AppStore.Core.Services;
using AppStore.Core.Winget;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AppStore;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Elevated helper mode: run the batch with no window, then exit.
        var batch = Array.IndexOf(args, "--run-batch");
        if (batch >= 0 && batch + 1 < args.Length)
        {
            return RunHelper(() => ElevatedHelper.RunBatchAsync(new WingetPackageManager(), args[batch + 1]));
        }

        var task = Array.IndexOf(args, "--run-task");
        if (task >= 0 && task + 1 < args.Length)
        {
            return RunHelper(() => ElevatedHelper.RunTaskAsync(new WingetPackageManager(), args[task + 1]));
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single instance: a second launch brings the open window to the front.
        var key = args.Contains("--test-data") ? "Sushant.AppStore.Test" : "Sushant.AppStore";
        var instance = AppInstance.FindOrRegisterForKey(key);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(async () => await instance.RedirectActivationToAsync(activation)).Wait(TimeSpan.FromSeconds(5));
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args);
        });
        return 0;
    }

    private static int RunHelper(Func<Task<int>> work)
    {
        try
        {
            return work().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            CrashLog.Write(ex);
            Debug.WriteLine(ex);
            return 2;
        }
    }
}
