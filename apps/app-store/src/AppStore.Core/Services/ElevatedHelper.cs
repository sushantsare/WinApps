using System.Text.Json;
using AppStore.Core.Models;
using AppStore.Core.Winget;

namespace AppStore.Core.Services;

/// <summary>
/// The elevated side of "one admin popup per batch". App Store starts a second copy of itself as admin with
/// <c>--run-batch &lt;folder&gt;</c>; that copy runs every request and writes one JSON line per event to
/// <c>events.jsonl</c>. The normal app reads those lines and can ask it to stop through <c>cancel.txt</c>.
/// </summary>
public static class ElevatedHelper
{
    public const string RequestsFile = "requests.json";
    public const string EventsFile = "events.jsonl";
    public const string CancelFile = "cancel.txt";
    public const string DoneFile = "done.txt";
    public const string TaskFile = "task.txt";
    public const string ResultFile = "result.json";

    public static async Task<int> RunBatchAsync(IPackageManager pm, string folder)
    {
        var requests = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(folder, RequestsFile)).ConfigureAwait(false), AppJson.Default.ListOperationRequest) ?? [];
        var events = Path.Combine(folder, EventsFile);
        var gate = new Lock();
        void Write(BatchEvent ev)
        {
            lock (gate)
            {
                File.AppendAllText(events, JsonSerializer.Serialize(ev, AppJson.Default.BatchEvent).ReplaceLineEndings(" ") + "\n");
            }
        }

        for (var i = 0; i < requests.Count; i++)
        {
            var cancelled = CancelledIndexes(folder);
            if (cancelled.Contains(-1))
            {
                break;
            }

            if (cancelled.Contains(i))
            {
                Write(new BatchEvent { Index = i, State = OpState.Cancelled, Message = "Cancelled" });
                continue;
            }

            using var cts = new CancellationTokenSource();
            var index = i;
            using var watcher = new Timer(_ =>
            {
                var c = CancelledIndexes(folder);
                if (c.Contains(-1) || c.Contains(index))
                {
                    cts.Cancel();
                }
            }, null, 500, 500);
            await BatchRunner.RunOneAsync(pm, requests[i], i, Write, cts.Token).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(folder, DoneFile), "done").ConfigureAwait(false);
        return 0;
    }

    /// <summary>Runs a single admin task ("fix-winget", "reset-sources") and writes its result.</summary>
    public static async Task<int> RunTaskAsync(IPackageManager pm, string folder)
    {
        var task = (await File.ReadAllTextAsync(Path.Combine(folder, TaskFile)).ConfigureAwait(false)).Trim();
        var result = task switch
        {
            "fix-winget" => await pm.RepairAsync(new Progress<OpProgress>(), CancellationToken.None).ConfigureAwait(false),
            "reset-sources" => await pm.ResetSourcesAsync(CancellationToken.None).ConfigureAwait(false),
            _ => OpResult.Fail("Unknown task " + task),
        };
        var ev = new BatchEvent { State = result.Success ? OpState.Done : OpState.Failed, Message = result.Message };
        await File.WriteAllTextAsync(Path.Combine(folder, ResultFile), JsonSerializer.Serialize(ev, AppJson.Default.BatchEvent)).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, DoneFile), "done").ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    public static HashSet<int> CancelledIndexes(string folder)
    {
        var set = new HashSet<int>();
        try
        {
            var f = Path.Combine(folder, CancelFile);
            if (File.Exists(f))
            {
                foreach (var line in File.ReadAllLines(f))
                {
                    if (int.TryParse(line.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    {
                        set.Add(n);
                    }
                }
            }
        }
        catch (IOException)
        {
        }

        return set;
    }

    public static string WriteRequests(IReadOnlyList<OperationRequest> requests)
    {
        var folder = Path.Combine(AppPaths.Batches, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, RequestsFile), JsonSerializer.Serialize(requests.ToList(), AppJson.Default.ListOperationRequest));
        File.WriteAllText(Path.Combine(folder, EventsFile), string.Empty);
        return folder;
    }

    public static string WriteTask(string task)
    {
        var folder = Path.Combine(AppPaths.Batches, "task-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TaskFile), task);
        return folder;
    }

    public static BatchEvent? ParseEvent(string line)
    {
        try
        {
            return line.Trim().Length == 0 ? null : JsonSerializer.Deserialize(line, AppJson.Default.BatchEvent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static BatchEvent? ReadResult(string folder)
    {
        var f = Path.Combine(folder, ResultFile);
        return File.Exists(f) ? ParseEvent(File.ReadAllText(f)) : null;
    }

    /// <summary>Deletes batch folders older than a day.</summary>
    public static void CleanOld()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Batches))
            {
                return;
            }

            foreach (var d in Directory.GetDirectories(AppPaths.Batches))
            {
                if (Directory.GetCreationTime(d) < DateTime.Now.AddDays(-1))
                {
                    Directory.Delete(d, recursive: true);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
