using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IPrinterInstallService
{
    Task<PrinterInstallResult> InstallPrinterAsync(PrinterDevice printer, CancellationToken cancellationToken);
}
