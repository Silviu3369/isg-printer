using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// SNMP entry point for the rest of the app: it pulls the active profile and
/// the "SNMP enabled" flag straight from settings, so callers only pass an IP.
/// This is how modules consume SNMP "automatically from Settings".
/// </summary>
public interface ISnmpQueryService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    Task<SnmpResult<TonerStatus>> GetTonerAsync(string ipAddress, CancellationToken cancellationToken);

    Task<SnmpResult<PrinterHardwareInfo>> GetHardwareAsync(string ipAddress, CancellationToken cancellationToken);

    Task<SnmpResult<long?>> GetPageCountAsync(string ipAddress, CancellationToken cancellationToken);

    Task<SnmpResult<string>> GetStatusAsync(string ipAddress, CancellationToken cancellationToken);
}
