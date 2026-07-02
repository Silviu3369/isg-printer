using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IPrinterDiagnosticsService
{
    Task<PrinterDiagnosticResult> DiagnoseAsync(PrinterDevice printer, CancellationToken cancellationToken);
}
