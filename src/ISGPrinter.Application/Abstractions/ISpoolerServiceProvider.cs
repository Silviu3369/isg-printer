using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface ISpoolerServiceProvider
{
    Task<PrinterDiagnosticCheck> CheckSpoolerAsync(CancellationToken cancellationToken);

    Task<OperationResult> RestartSpoolerAsync(CancellationToken cancellationToken);
}
