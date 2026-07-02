using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Diagnostics;

public sealed class PrinterDiagnosticsService(
    IAppEnvironmentService environmentService,
    ILocalPrinterService localPrinterService,
    ISpoolerServiceProvider spoolerServiceProvider,
    INetworkProbeProvider networkProbeProvider,
    IPrintEventLogProvider printEventLogProvider,
    ISnmpQueryService snmpQueryService,
    ISettingsService settingsService,
    ITechnicianGuidanceService guidanceService) : IPrinterDiagnosticsService
{
    private sealed record NetworkTargets(string? PrinterDeviceTarget, string? PrintServerTarget);

    public async Task<PrinterDiagnosticResult> DiagnoseAsync(PrinterDevice printer, CancellationToken cancellationToken)
    {
        var result = new PrinterDiagnosticResult
        {
            PrinterName = printer.Name,
            UncPath = printer.UncPath,
            CreatedAt = DateTimeOffset.Now
        };

        // --- Layer 1: environment ---
        var environment = await environmentService.GetEnvironmentAsync(cancellationToken);
        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Administrator",
            Status = environment.IsElevated ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            Message = environment.IsElevated
                ? "Application is running elevated."
                : "Application is not running elevated."
        });

        // --- Layer 2: spooler ---
        result.Checks.Add(await spoolerServiceProvider.CheckSpoolerAsync(cancellationToken));

        // --- Layer 3: local install / driver / queue ---
        var localPrinters = await localPrinterService.GetLocalPrintersAsync(cancellationToken);
        var installed = localPrinters.FirstOrDefault(local =>
            string.Equals(local.Name, printer.Name, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(printer.UncPath) && string.Equals(local.UncPath, printer.UncPath, StringComparison.OrdinalIgnoreCase)));

        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Installed locally",
            Status = installed is not null ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
            Message = installed is not null
                ? "Printer is installed for the current Windows account."
                : "Printer is not installed for the current Windows account."
        });

        if (installed is not null)
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Default printer",
                Status = installed.IsDefault ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                Message = installed.IsDefault
                    ? "Printer is currently the default printer."
                    : "Printer is installed but not default."
            });

            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Driver",
                Status = string.IsNullOrWhiteSpace(installed.DriverName) ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                Message = string.IsNullOrWhiteSpace(installed.DriverName)
                    ? "Driver name is not available."
                    : $"Driver: {installed.DriverName}"
            });

            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Queue",
                Status = installed.WorkOffline ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                Message = installed.WorkOffline
                    ? "Printer is marked as working offline."
                    : "Printer is not marked offline."
            });
        }

        // --- Layer 4: network reachability ---
        var targets = await ResolveNetworkTargetsAsync(printer, installed, cancellationToken);
        await AddNetworkChecksAsync(result, targets, cancellationToken);

        // --- Layer 5: supplies & device status over SNMP ---
        await AddSnmpChecksAsync(result, targets.PrinterDeviceTarget, cancellationToken);

        // --- Layer 6: recent print errors ---
        await AddEventLogCheckAsync(result, cancellationToken);

        result.OverallStatus = ResolveOverallStatus(result.Checks);
        result.RecommendedSteps = guidanceService.BuildRecommendedSteps(result).ToList();
        return result;
    }

    private async Task AddNetworkChecksAsync(
        PrinterDiagnosticResult result,
        NetworkTargets targets,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targets.PrinterDeviceTarget)
            && string.IsNullOrWhiteSpace(targets.PrintServerTarget))
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Network",
                Status = DiagnosticStatus.Ok,
                Message = "Local or virtual printer — no network target to test."
            });
            return;
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        var timeout = settings.NetworkTimeoutMs > 0 ? settings.NetworkTimeoutMs : 2000;

        if (!string.IsNullOrWhiteSpace(targets.PrintServerTarget))
        {
            await AddPrintServerChecksAsync(result, targets.PrintServerTarget, timeout, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(targets.PrinterDeviceTarget))
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Printer device",
                Status = DiagnosticStatus.Unknown,
                Message = "No printer device IP/host is available; raw port and SNMP checks are skipped for this shared queue."
            });
            return;
        }

        await AddPrinterDeviceChecksAsync(result, targets.PrinterDeviceTarget, settings, timeout, cancellationToken);
    }

    private async Task AddPrintServerChecksAsync(
        PrinterDiagnosticResult result,
        string printServer,
        int timeout,
        CancellationToken cancellationToken)
    {
        var dnsTask = networkProbeProvider.ResolveDnsAsync(printServer, cancellationToken);
        var pingTask = networkProbeProvider.PingAsync(printServer, timeout, cancellationToken);
        await Task.WhenAll(dnsTask, pingTask);

        var dns = await dnsTask;
        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Print server",
            Status = dns.Success ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            Message = dns.Success
                ? $"Print server {printServer} resolves in DNS."
                : $"Print server {printServer} could not be resolved.",
            TechnicalDetails = dns.TechnicalDetails
        });

        var ping = await pingTask;
        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Print server ping",
            Status = ping.Success ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
            Message = ping.Success
                ? $"{printServer} responds to ping ({ping.Elapsed.TotalMilliseconds:F0} ms)."
                : $"{printServer} did not respond to ping (ICMP may be blocked even if printing works).",
            TechnicalDetails = ping.TechnicalDetails
        });
    }

    private async Task AddPrinterDeviceChecksAsync(
        PrinterDiagnosticResult result,
        string target,
        AppSettings settings,
        int timeout,
        CancellationToken cancellationToken)
    {
        // Full timeout kept for robustness on slow/VPN links; speed comes from
        // probing ping and all ports in parallel.

        var ports = settings.TcpPortsToCheck is { Count: > 0 }
            ? settings.TcpPortsToCheck.Distinct().ToList()
            : [9100, 631];

        // Run ping and all port probes concurrently — the network layer now
        // takes ~one timeout instead of the sum of every probe.
        var pingTask = networkProbeProvider.PingAsync(target, timeout, cancellationToken);
        var portTasks = ports
            .Select(port => (port, task: networkProbeProvider.CheckTcpPortAsync(target, port, timeout, cancellationToken)))
            .ToList();

        await Task.WhenAll(portTasks.Select(item => item.task).Append(pingTask));

        var ping = await pingTask;
        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Ping",
            Status = ping.Success ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
            Message = ping.Success
                ? $"{target} responds to ping ({ping.Elapsed.TotalMilliseconds:F0} ms)."
                : $"{target} did not respond to ping (ICMP may be blocked even if printing works).",
            TechnicalDetails = ping.TechnicalDetails
        });

        var portResults = new List<string>();
        var anyReachable = false;
        foreach (var (port, task) in portTasks)
        {
            var probe = await task;
            portResults.Add($"{port}: {(probe.Success ? "open" : "closed")}");
            anyReachable |= probe.Success;
        }

        result.Checks.Add(new PrinterDiagnosticCheck
        {
            Name = "Print ports",
            Status = anyReachable ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            Message = anyReachable
                ? $"Reachable on {target} ({string.Join(", ", portResults)})."
                : $"No print port reachable on {target} ({string.Join(", ", portResults)})."
        });
    }

    private async Task AddSnmpChecksAsync(PrinterDiagnosticResult result, string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target) || !await snmpQueryService.IsEnabledAsync(cancellationToken))
        {
            return;
        }

        var toner = await snmpQueryService.GetTonerAsync(target, cancellationToken);
        if (toner is { Success: true, Value: { IsAvailable: true } supplies })
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Supplies",
                Status = MapTonerStatus(supplies.State),
                Message = DescribeToner(supplies)
            });
        }
        else
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Supplies",
                Status = DiagnosticStatus.Unknown,
                Message = "Toner levels could not be read over SNMP.",
                TechnicalDetails = toner.Message
            });
        }

        var status = await snmpQueryService.GetStatusAsync(target, cancellationToken);
        if (status is { Success: true, Value: { Length: > 0 } statusText })
        {
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Device status",
                Status = DiagnosticStatus.Ok,
                Message = $"SNMP device status: {statusText}."
            });
        }
    }

    private static DiagnosticStatus MapTonerStatus(TonerLevelState state) => state switch
    {
        TonerLevelState.Low => DiagnosticStatus.Warning,
        TonerLevelState.Critical or TonerLevelState.Empty => DiagnosticStatus.Error,
        _ => DiagnosticStatus.Ok
    };

    private static string DescribeToner(TonerStatus toner)
    {
        var parts = new List<string>();
        if (toner.BlackPercent.HasValue)
        {
            parts.Add($"Black {toner.BlackPercent}%");
        }

        if (toner.CyanPercent.HasValue)
        {
            parts.Add($"Cyan {toner.CyanPercent}%");
        }

        if (toner.MagentaPercent.HasValue)
        {
            parts.Add($"Magenta {toner.MagentaPercent}%");
        }

        if (toner.YellowPercent.HasValue)
        {
            parts.Add($"Yellow {toner.YellowPercent}%");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Supplies reported (levels not available).";
    }

    private async Task<NetworkTargets> ResolveNetworkTargetsAsync(
        PrinterDevice printer,
        LocalPrinter? installed,
        CancellationToken cancellationToken)
    {
        var printServerTarget = ExtractServer(printer.UncPath) ?? ExtractServer(installed?.UncPath);

        if (!string.IsNullOrWhiteSpace(printer.IpAddress))
        {
            return new NetworkTargets(printer.IpAddress, printServerTarget);
        }

        var portName = installed?.PortName ?? printer.PortName;
        if (!string.IsNullOrWhiteSpace(portName))
        {
            var host = await localPrinterService.GetPortHostAddressAsync(portName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(host))
            {
                return new NetworkTargets(host, printServerTarget);
            }
        }

        return new NetworkTargets(null, printServerTarget);
    }

    private async Task AddEventLogCheckAsync(PrinterDiagnosticResult result, CancellationToken cancellationToken)
    {
        try
        {
            var events = await printEventLogProvider.GetRecentPrintErrorsAsync(TimeSpan.FromHours(24), 25, cancellationToken);

            if (events.Count == 0)
            {
                result.Checks.Add(new PrinterDiagnosticCheck
                {
                    Name = "Print errors",
                    Status = DiagnosticStatus.Ok,
                    Message = "No print errors logged in the last 24 hours."
                });
                return;
            }

            var latest = events[0];
            result.Checks.Add(new PrinterDiagnosticCheck
            {
                Name = "Print errors",
                Status = DiagnosticStatus.Warning,
                Message = $"{events.Count} print error(s) logged in the last 24 hours.",
                TechnicalDetails = $"Latest [{latest.Time.LocalDateTime:g}]: {latest.Message}"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The event-log layer is best-effort; never fail the whole diagnosis for it.
        }
    }

    private static string? ExtractServer(string? uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = uncPath[2..];
        var slash = rest.IndexOf('\\');
        var server = slash >= 0 ? rest[..slash] : rest;
        return string.IsNullOrWhiteSpace(server) ? null : server;
    }

    private static DiagnosticStatus ResolveOverallStatus(IEnumerable<PrinterDiagnosticCheck> checks)
    {
        var checkList = checks.ToList();
        if (checkList.Any(check => check.Status == DiagnosticStatus.Error))
        {
            return DiagnosticStatus.Error;
        }

        if (checkList.Any(check => check.Status == DiagnosticStatus.Warning))
        {
            return DiagnosticStatus.Warning;
        }

        return checkList.Count == 0 ? DiagnosticStatus.Unknown : DiagnosticStatus.Ok;
    }
}
