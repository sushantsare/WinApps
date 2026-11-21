using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using AppStore.Core.Models;
using AppStore.Core.Services;

namespace AppStore.Services;

/// <summary>Starts an elevated copy of App Store (one Windows admin popup) to run a whole batch.</summary>
public sealed class ElevationService : IElevation
{
    private const int ErrorCancelled = 1223;
    private string? _currentFolder;

    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public async Task<bool> RunBatchAsync(IReadOnlyList<OperationRequest> requests, Action<BatchEvent> onEvent, CancellationToken ct)
    {
        var folder = ElevatedHelper.WriteRequests(requests);
        var proc = StartElevated("--run-batch", folder);
        if (proc is null)
        {
            return false;
        }

        _currentFolder = folder;
        var events = Path.Combine(folder, ElevatedHelper.EventsFile);
        long offset = 0;
        var finished = new HashSet<int>();
        try
        {
            using (proc)
            {
                void Handle(BatchEvent ev)
                {
                    if (ev.State is OpState.Done or OpState.Failed or OpState.Cancelled or OpState.Skipped)
                    {
                        finished.Add(ev.Index);
                    }

                    onEvent(ev);
                }

                while (true)
                {
                    offset = ReadEvents(events, offset, Handle);
                    if (proc.HasExited)
                    {
                        ReadEvents(events, offset, Handle);
                        break;
                    }

                    await Task.Delay(300, ct).ConfigureAwait(false);
                }
            }

            // If the helper stopped early (crash, PC sleeping…), tell the user which items never finished.
            var done = File.Exists(Path.Combine(folder, ElevatedHelper.DoneFile));
            var cancelled = ElevatedHelper.CancelledIndexes(folder);
            for (var i = 0; i < requests.Count; i++)
            {
                if (!finished.Contains(i))
                {
                    onEvent(cancelled.Contains(i) || cancelled.Contains(-1)
                        ? new BatchEvent { Index = i, State = OpState.Cancelled, Message = "Cancelled" }
                        : new BatchEvent { Index = i, State = OpState.Failed, Message = done ? "Didn't run." : "The admin helper stopped before finishing this app. Try again." });
                }
            }

            return true;
        }
        finally
        {
            _currentFolder = null;
        }
    }

    public void Cancel(int index)
    {
        var folder = _currentFolder;
        if (folder is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(Path.Combine(folder, ElevatedHelper.CancelFile), index.ToString(System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine);
        }
        catch (IOException)
        {
        }
    }

    public async Task<OpResult?> RunElevatedTaskAsync(string task, CancellationToken ct)
    {
        var folder = ElevatedHelper.WriteTask(task);
        var proc = StartElevated("--run-task", folder);
        if (proc is null)
        {
            return null;
        }

        using (proc)
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        var ev = ElevatedHelper.ReadResult(folder);
        return ev is null
            ? OpResult.Fail("The admin helper stopped before it finished.")
            : ev.State == OpState.Done ? OpResult.Ok(ev.Message) : OpResult.Fail(ev.Message);
    }

    private static Process? StartElevated(string verb, string folder)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = verb + " \"" + folder + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }

    private static long ReadEvents(string file, long offset, Action<BatchEvent> onEvent)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length <= offset)
            {
                return offset;
            }

            fs.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0)
            {
                return offset;
            }

            foreach (var line in text[..lastNewline].Split('\n'))
            {
                if (ElevatedHelper.ParseEvent(line) is { } ev)
                {
                    onEvent(ev);
                }
            }

            return offset + System.Text.Encoding.UTF8.GetByteCount(text[..(lastNewline + 1)]);
        }
        catch (IOException)
        {
            return offset;
        }
    }
}
