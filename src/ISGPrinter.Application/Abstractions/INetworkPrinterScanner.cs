using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// Scans the local subnet(s) for direct-IP network printers (devices that
/// answer on the raw print port 9100), enriching them over SNMP when possible.
/// </summary>
public interface INetworkPrinterScanner
{
    Task<IReadOnlyList<DiscoveredNetworkPrinter>> ScanAsync(CancellationToken cancellationToken);
}
