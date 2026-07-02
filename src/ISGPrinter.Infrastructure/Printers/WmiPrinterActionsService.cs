using System.Management;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

/// <summary>
/// Print-queue and printer actions backed by WMI (<c>Win32_PrintJob</c>,
/// <c>Win32_Printer</c>) plus <c>Remove-Printer</c> for uninstall. All work
/// runs on a background thread so the UI never blocks on WMI.
/// </summary>
public sealed class WmiPrinterActionsService : IPrinterActionsService
{
    private const string RemoveScript = """
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        try {
            Remove-Printer -Name $env:ISG_PRINTER
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 2
        }
        """;

    public Task<IReadOnlyList<PrintJob>> GetJobsAsync(string printerName, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<PrintJob>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var jobs = new List<PrintJob>();
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");

            foreach (var job in searcher.Get().Cast<ManagementObject>())
            {
                using (job)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (owningPrinter, jobId) = SplitJobName(GetString(job, "Name"));
                    if (!string.Equals(owningPrinter, printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    jobs.Add(new PrintJob
                    {
                        Id = jobId,
                        Document = GetString(job, "Document"),
                        Owner = GetString(job, "Owner"),
                        TotalPages = GetInt(job, "TotalPages"),
                        PagesPrinted = GetInt(job, "PagesPrinted"),
                        Status = ResolveJobStatus(GetString(job, "JobStatus")),
                        SubmittedAt = ParseCimDate(GetString(job, "TimeSubmitted"))
                    });
                }
            }

            return jobs.OrderBy(job => job.Id).ToList();
        }, cancellationToken);

    public Task<OperationResult> CancelJobAsync(string printerName, int jobId, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");
                foreach (var job in searcher.Get().Cast<ManagementObject>())
                {
                    using (job)
                    {
                        var (owningPrinter, currentId) = SplitJobName(GetString(job, "Name"));
                        if (currentId == jobId && string.Equals(owningPrinter, printerName, StringComparison.OrdinalIgnoreCase))
                        {
                            job.Delete();
                            return OperationResult.Ok("Job cancelled.");
                        }
                    }
                }

                return OperationResult.Fail(OperationStatus.NotFound, "That job is no longer in the queue.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(OperationStatus.Failed, "Could not cancel the job.", ex.Message);
            }
        }, cancellationToken);

    public Task<OperationResult> ClearQueueAsync(string printerName, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var cleared = 0;
                var failures = 0;

                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");
                foreach (var job in searcher.Get().Cast<ManagementObject>())
                {
                    using (job)
                    {
                        var (owningPrinter, _) = SplitJobName(GetString(job, "Name"));
                        if (!string.Equals(owningPrinter, printerName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            job.Delete();
                            cleared++;
                        }
                        catch
                        {
                            failures++;
                        }
                    }
                }

                if (failures > 0)
                {
                    return OperationResult.Fail(
                        OperationStatus.Failed,
                        $"Cleared {cleared} job(s); {failures} could not be removed. Try restarting the Print Spooler.");
                }

                return OperationResult.Ok(cleared == 0 ? "The queue was already empty." : $"Cleared {cleared} job(s) from the queue.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(OperationStatus.Failed, "Could not clear the queue.", ex.Message);
            }
        }, cancellationToken);

    public Task<OperationResult> PrintTestPageAsync(string printerName, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name = '{EscapeWql(printerName)}'");
                using var printer = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

                if (printer is null)
                {
                    return OperationResult.Fail(OperationStatus.NotFound, "Printer could not be found through WMI.");
                }

                var returnValue = Convert.ToUInt32(printer.InvokeMethod("PrintTestPage", null));
                return returnValue == 0
                    ? OperationResult.Ok("Test page sent.")
                    : OperationResult.Fail(OperationStatus.Failed, "Windows did not accept the test page request.", $"WMI return value: {returnValue}");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(OperationStatus.Failed, "Could not print a test page.", ex.Message);
            }
        }, cancellationToken);

    public async Task<OperationResult> RemovePrinterAsync(string printerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return OperationResult.Fail(OperationStatus.InvalidInput, "Printer name is required.");
        }

        var run = await PowerShellRunner.RunAsync(
            RemoveScript,
            new Dictionary<string, string> { ["ISG_PRINTER"] = printerName },
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (run.TimedOut)
        {
            return OperationResult.Fail(OperationStatus.Unavailable, "Removing the printer timed out.");
        }

        if (!run.Succeeded)
        {
            var details = string.IsNullOrWhiteSpace(run.StandardError) ? run.StandardOutput : run.StandardError;
            return OperationResult.Fail(OperationStatus.Failed, "Windows could not remove this printer.", details.Trim());
        }

        return OperationResult.Ok("Printer removed.");
    }

    private static (string printer, int id) SplitJobName(string name)
    {
        // Win32_PrintJob.Name is "<PrinterName>,<JobId>"; the printer name can
        // itself contain commas, so split on the last one.
        var index = name.LastIndexOf(',');
        if (index < 0)
        {
            return (name, 0);
        }

        var printer = name[..index];
        return int.TryParse(name[(index + 1)..], out var id) ? (printer, id) : (printer, 0);
    }

    private static string ResolveJobStatus(string jobStatus) =>
        string.IsNullOrWhiteSpace(jobStatus) ? "Spooled" : jobStatus;

    private static DateTimeOffset? ParseCimDate(string cimDate)
    {
        if (string.IsNullOrWhiteSpace(cimDate))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(cimDate);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string GetString(ManagementBaseObject source, string propertyName) =>
        source.Properties[propertyName]?.Value?.ToString() ?? string.Empty;

    private static int? GetInt(ManagementBaseObject source, string propertyName)
    {
        var value = source.Properties[propertyName]?.Value;
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
