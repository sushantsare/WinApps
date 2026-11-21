using System.Diagnostics;
using System.Text;

namespace AppStore.Core.Winget;

public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut);

/// <summary>Runs a console program hidden, streams its output line by line (splitting on \r too, for progress bars).</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        TimeSpan timeout,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        var all = new StringBuilder();
        proc.Start();
        proc.StandardInput.Close();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var outTask = PumpAsync(proc.StandardOutput, all, onLine, linked.Token);
        var errTask = PumpAsync(proc.StandardError, all, null, linked.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, all.ToString(), TimedOut: true);
        }

        return new ProcessResult(proc.ExitCode, all.ToString(), TimedOut: false);
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder all, Action<string>? onLine, CancellationToken ct)
    {
        var buf = new char[4096];
        var line = new StringBuilder();
        var pendingCr = false;
        int n;
        while ((n = await reader.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                var c = buf[i];
                if (pendingCr)
                {
                    pendingCr = false;
                    if (c == '\n')
                    {
                        Flush(line, all, onLine, newline: true);
                        continue;
                    }

                    // A lone \r: the program redrew the line (progress bar).
                    Flush(line, all, onLine, newline: false);
                }

                if (c == '\r')
                {
                    pendingCr = true;
                }
                else if (c == '\n')
                {
                    Flush(line, all, onLine, newline: true);
                }
                else
                {
                    line.Append(c);
                }
            }
        }

        Flush(line, all, onLine, newline: true);
    }

    private static void Flush(StringBuilder line, StringBuilder all, Action<string>? onLine, bool newline)
    {
        if (line.Length == 0)
        {
            if (newline)
            {
                lock (all)
                {
                    all.Append('\n');
                }
            }

            return;
        }

        var s = line.ToString();
        line.Clear();
        lock (all)
        {
            // Progress redraws end with \r: keep only finished lines in the full output.
            if (newline)
            {
                all.Append(s).Append('\n');
            }
        }

        onLine?.Invoke(s);
    }
}
