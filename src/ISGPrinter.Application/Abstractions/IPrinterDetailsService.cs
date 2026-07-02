using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IPrinterDetailsService
{
    Task<PrinterDevice?> GetDetailsAsync(string printerNameOrUncPath, CancellationToken cancellationToken);
}
