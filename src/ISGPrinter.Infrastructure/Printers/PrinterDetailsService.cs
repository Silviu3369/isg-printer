using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class PrinterDetailsService(ILocalPrinterService localPrinterService) : IPrinterDetailsService
{
    public async Task<PrinterDevice?> GetDetailsAsync(string printerNameOrUncPath, CancellationToken cancellationToken)
    {
        var printers = await localPrinterService.GetLocalPrintersAsync(cancellationToken);
        var localPrinter = printers.FirstOrDefault(printer =>
            string.Equals(printer.Name, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(printer.UncPath, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase));

        if (localPrinter is null)
        {
            return null;
        }

        return new PrinterDevice
        {
            Id = localPrinter.UncPath.Length > 0 ? localPrinter.UncPath : localPrinter.Name,
            Name = localPrinter.Name,
            UncPath = localPrinter.UncPath,
            ServerName = localPrinter.ServerName,
            DriverName = localPrinter.DriverName,
            PortName = localPrinter.PortName,
            IsDefault = localPrinter.IsDefault,
            IsInstalledLocally = true
        };
    }
}
