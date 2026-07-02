using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IPrinterReportService
{
    string GenerateTicketReport(PrinterDiagnosticResult result, AppEnvironmentInfo environment);

    Task<OperationResult> ExportReportAsync(string reportContent, string filePath, CancellationToken cancellationToken);
}
