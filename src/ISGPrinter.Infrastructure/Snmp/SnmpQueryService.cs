using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Snmp;

/// <summary>
/// Resolves the active SNMP profile and enabled flag from settings, then
/// delegates to the low-level provider. Callers (Printers, Diagnostics) only
/// supply an IP — credentials always come from Settings, configured once.
/// </summary>
public sealed class SnmpQueryService(
    ISettingsService settingsService,
    ISnmpPrinterProvider provider) : ISnmpQueryService
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return settings.EnableSnmp;
    }

    public Task<SnmpResult<TonerStatus>> GetTonerAsync(string ipAddress, CancellationToken cancellationToken) =>
        RunAsync(ipAddress, cancellationToken, SnmpResult<TonerStatus>.Unavailable,
            (profile, ip, timeout) => provider.GetTonerStatusAsync(ip, profile, timeout, cancellationToken));

    public Task<SnmpResult<PrinterHardwareInfo>> GetHardwareAsync(string ipAddress, CancellationToken cancellationToken) =>
        RunAsync(ipAddress, cancellationToken, SnmpResult<PrinterHardwareInfo>.Unavailable,
            (profile, ip, timeout) => provider.GetHardwareInfoAsync(ip, profile, timeout, cancellationToken));

    public Task<SnmpResult<long?>> GetPageCountAsync(string ipAddress, CancellationToken cancellationToken) =>
        RunAsync(ipAddress, cancellationToken, SnmpResult<long?>.Unavailable,
            (profile, ip, timeout) => provider.GetPageCounterAsync(ip, profile, timeout, cancellationToken));

    public Task<SnmpResult<string>> GetStatusAsync(string ipAddress, CancellationToken cancellationToken) =>
        RunAsync(ipAddress, cancellationToken, SnmpResult<string>.Unavailable,
            (profile, ip, timeout) => provider.GetRawPrinterStatusAsync(ip, profile, timeout, cancellationToken));

    private async Task<SnmpResult<T>> RunAsync<T>(
        string ipAddress,
        CancellationToken cancellationToken,
        Func<string, SnmpResult<T>> unavailable,
        Func<SnmpProfile, string, TimeSpan, Task<SnmpResult<T>>> query)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return unavailable("No IP address is known for this printer.");
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        if (!settings.EnableSnmp)
        {
            return unavailable("SNMP is disabled in Settings.");
        }

        var profile = settings.SnmpProfiles.FirstOrDefault(p =>
                          string.Equals(p.Name, settings.DefaultSnmpProfile, StringComparison.OrdinalIgnoreCase))
                          ?? settings.SnmpProfiles.FirstOrDefault()
                          ?? SnmpProfile.DefaultV2();

        return await query(profile, ipAddress, ResolveTimeout(settings.SnmpTimeoutMs));
    }

    private static TimeSpan ResolveTimeout(int configuredMilliseconds)
    {
        var milliseconds = configuredMilliseconds > 0 ? configuredMilliseconds : 2500;
        return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 500, 15000));
    }
}
