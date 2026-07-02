using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface ITechnicianGuidanceService
{
    IReadOnlyList<string> BuildRecommendedSteps(PrinterDiagnosticResult diagnosticResult);
}
