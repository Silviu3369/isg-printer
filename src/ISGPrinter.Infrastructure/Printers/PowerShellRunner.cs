using System.Diagnostics;
using System.Text;

namespace ISGPrinter.Infrastructure.Printers;

/// <summary>
/// Runs a Windows PowerShell command robustly:
/// <list type="bullet">
/// <item>stdout and stderr are read concurrently while waiting, so a large
/// payload can never deadlock the pipe;</item>
/// <item>a hard timeout kills the (whole) process tree if a remote call hangs
/// — e.g. <c>Get-Printer -ComputerName</c> against a firewalled host;</item>
/// <item>inputs are passed as environment variables, never concatenated into
/// the script, so a hostile printer/server name cannot inject code.</item>
/// </list>
/// </summary>
internal static class PowerShellRunner
{
    public sealed record Result(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    public static async Task<Result> RunAsync(
        string script,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new Result(-1, string.Empty, ex.Message, TimedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // A caller-driven cancel propagates; a timeout is reported as data.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new Result(-1, string.Empty, string.Empty, TimedOut: true);
        }

        var stdout = await ReadSafelyAsync(stdoutTask);
        var stderr = await ReadSafelyAsync(stderrTask);
        return new Result(process.ExitCode, stdout, stderr, TimedOut: false);
    }

    private static async Task<string> ReadSafelyAsync(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort — the process may have exited between the check and the kill.
        }
    }
}
