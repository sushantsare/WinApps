using System.Diagnostics;
using System.Text;

namespace AppStore.Core.Winget;

/// <summary>
/// One PowerShell process kept open in the background with Microsoft's WinGet module already loaded, so each
/// search or list costs only the query itself (no 1–3 s start-up per call). Commands run one at a time; a command
/// that hangs is stopped and a fresh session starts on the next call.
/// </summary>
public sealed class PsSession : IDisposable
{
    private const string Setup =
        "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8;[Console]::InputEncoding=[Text.Encoding]::UTF8;" +
        "Import-Module Microsoft.WinGet.Client";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _proc;

    public string LastError { get; private set; } = string.Empty;

    public bool IsRunning => _proc is { HasExited: false };

    /// <summary>Starts the session (and loads the module) ahead of the first search.</summary>
    public async Task<bool> WarmUpAsync(CancellationToken ct) => await RunAsync("'ready'", TimeSpan.FromSeconds(60), ct).ConfigureAwait(false) is not null;

    /// <summary>Runs a one-line script and returns everything it printed, or null on error/timeout.</summary>
    public async Task<string?> RunAsync(string script, TimeSpan timeout, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsRunning && !await StartAsync().ConfigureAwait(false))
            {
                return null;
            }

            return await SendAsync(script, timeout).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> StartAsync()
    {
        Stop();
        var psi = new ProcessStartInfo(WingetPowerShell.PowerShellExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in new[] { "-NoProfile", "-NoLogo", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "-" })
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            _proc = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LastError = ex.Message;
            return false;
        }

        if (_proc is null)
        {
            return false;
        }

        // Drain stderr so the process never blocks on a full error pipe.
        var started = _proc;
        _ = Task.Run(async () =>
        {
            try
            {
                while (await started.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    LastError = line;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
        });

        return await SendAsync(Setup + ";'ok'", TimeSpan.FromSeconds(60)).ConfigureAwait(false) is { } r && r.Contains("ok", StringComparison.Ordinal);
    }

    private async Task<string?> SendAsync(string script, TimeSpan timeout)
    {
        var proc = _proc!;
        var marker = "##END-" + Guid.NewGuid().ToString("N") + "##";
        var line = "try { " + script + " } catch { '##ERR## ' + $_.Exception.Message }; '" + marker + "'";
        try
        {
            await proc.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await proc.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            LastError = ex.Message;
            Stop();
            return null;
        }

        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var l = await proc.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (l is null)
                {
                    Stop();
                    return null;
                }

                if (l.Contains(marker, StringComparison.Ordinal))
                {
                    break;
                }

                if (l.StartsWith("##ERR## ", StringComparison.Ordinal))
                {
                    LastError = l[8..];
                    sb.Clear();
                    sb.Append("##ERR##");
                    continue;
                }

                sb.AppendLine(l);
            }
        }
        catch (OperationCanceledException)
        {
            LastError = "PowerShell didn't answer within " + timeout.TotalSeconds + " s.";
            Stop();
            return null;
        }

        var text = sb.ToString();
        return text.StartsWith("##ERR##", StringComparison.Ordinal) ? null : text;
    }

    private void Stop()
    {
        try
        {
            if (_proc is { HasExited: false })
            {
                _proc.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        _proc?.Dispose();
        _proc = null;
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}
