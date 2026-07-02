using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class PrinterInstallService(ILocalPrinterService localPrinterService) : IPrinterInstallService
{
    // Driver staging from the server can take a while, so allow generous time.
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(90);

    // Connection name arrives via $env:ISG_CONNECTION — never interpolated.
    private const string InstallScript = """
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        try {
            Add-Printer -ConnectionName $env:ISG_CONNECTION
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 2
        }
        """;

    public async Task<PrinterInstallResult> InstallPrinterAsync(PrinterDevice printer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printer.UncPath) || !printer.UncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return Fail(OperationStatus.InvalidInput, PrinterInstallState.Failed, "This printer has no valid UNC path to install from.");
        }

        if (await localPrinterService.IsPrinterInstalledAsync(printer.UncPath, cancellationToken))
        {
            return new PrinterInstallResult
            {
                Success = true,
                InstallState = PrinterInstallState.AlreadyInstalled,
                Status = OperationStatus.AlreadyExists,
                Message = "Printer is already installed for the current Windows account."
            };
        }

        var run = await PowerShellRunner.RunAsync(
            InstallScript,
            new Dictionary<string, string> { ["ISG_CONNECTION"] = printer.UncPath },
            InstallTimeout,
            cancellationToken);

        if (run.TimedOut)
        {
            return Fail(
                OperationStatus.Unavailable,
                PrinterInstallState.Failed,
                "Installation timed out. The print server may be slow or the driver download stalled.");
        }

        if (!run.Succeeded)
        {
            var details = string.IsNullOrWhiteSpace(run.StandardError) ? run.StandardOutput : run.StandardError;
            return Fail(OperationStatus.Failed, PrinterInstallState.Failed, "Windows could not install this printer.", details.Trim());
        }

        var installed = await localPrinterService.IsPrinterInstalledAsync(printer.UncPath, cancellationToken);
        return installed
            ? new PrinterInstallResult
            {
                Success = true,
                InstallState = PrinterInstallState.Installed,
                Status = OperationStatus.Success,
                Message = "Printer installed for the current Windows account."
            }
            : Fail(OperationStatus.Unknown, PrinterInstallState.Unknown, "Windows reported success, but the printer was not found afterward.", run.StandardOutput.Trim());
    }

    private static PrinterInstallResult Fail(
        OperationStatus status,
        PrinterInstallState installState,
        string message,
        string technicalDetails = "") =>
        new()
        {
            Success = false,
            Status = status,
            InstallState = installState,
            Message = message,
            TechnicalDetails = technicalDetails
        };
}
