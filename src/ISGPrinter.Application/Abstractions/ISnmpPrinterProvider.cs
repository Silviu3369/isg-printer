using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface ISnmpPrinterProvider
{
    Task<SnmpResult<TonerStatus>> GetTonerStatusAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<SnmpResult<PrinterHardwareInfo>> GetHardwareInfoAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<SnmpResult<long?>> GetPageCounterAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<SnmpResult<string>> GetRawPrinterStatusAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
