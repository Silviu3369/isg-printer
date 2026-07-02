using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface ILocalPrinterService
{
    Task<IReadOnlyList<LocalPrinter>> GetLocalPrintersAsync(CancellationToken cancellationToken);

    Task<LocalPrinter?> GetDefaultPrinterAsync(CancellationToken cancellationToken);

    Task<bool> IsPrinterInstalledAsync(string printerNameOrUncPath, CancellationToken cancellationToken);

    /// <summary>Returns the host address (IP/DNS) of a TCP/IP printer port, or null for non-TCP ports.</summary>
    Task<string?> GetPortHostAddressAsync(string portName, CancellationToken cancellationToken);
}
