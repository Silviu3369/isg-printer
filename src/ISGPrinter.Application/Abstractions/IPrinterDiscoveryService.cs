using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IPrinterDiscoveryService
{
    Task<PrinterDiscoveryResult> DiscoverCompanyPrintersAsync(CancellationToken cancellationToken);

    Task<PrinterDiscoveryResult> DiscoverServerPrintersAsync(string serverName, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintServer>> DiscoverPrintServersAsync(CancellationToken cancellationToken);
}
