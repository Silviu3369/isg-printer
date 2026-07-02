using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// Technician actions against a locally installed printer: inspecting and
/// clearing the print queue, printing a test page, and removing the printer.
/// </summary>
public interface IPrinterActionsService
{
    Task<IReadOnlyList<PrintJob>> GetJobsAsync(string printerName, CancellationToken cancellationToken);

    Task<OperationResult> CancelJobAsync(string printerName, int jobId, CancellationToken cancellationToken);

    Task<OperationResult> ClearQueueAsync(string printerName, CancellationToken cancellationToken);

    Task<OperationResult> PrintTestPageAsync(string printerName, CancellationToken cancellationToken);

    Task<OperationResult> RemovePrinterAsync(string printerName, CancellationToken cancellationToken);
}
