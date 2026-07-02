using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

/// <summary>
/// Sweeps the local /24 subnet(s) for devices answering on the raw print port
/// (9100), in parallel, then enriches each hit with model/name over SNMP when
/// SNMP is enabled. Finds direct-IP printers that no print server shares.
/// </summary>
public sealed class NetworkPrinterScanner(
    INetworkProbeProvider networkProbeProvider,
    ISnmpQueryService snmpQueryService) : INetworkPrinterScanner
{
    private const int RawPrintPort = 9100;
    private const int ProbeTimeoutMs = 400;
    private const int MaxConcurrency = 64;

    public async Task<IReadOnlyList<DiscoveredNetworkPrinter>> ScanAsync(CancellationToken cancellationToken)
    {
        var found = new ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var probes = new List<Task>();
        foreach (var prefix in GetLocalSubnetPrefixes())
        {
            for (var host = 1; host <= 254; host++)
            {
                probes.Add(ProbeAsync($"{prefix}{host}", found, gate, cancellationToken));
            }
        }

        await Task.WhenAll(probes);

        // Enrich every hit over SNMP in parallel (bounded). Sequentially this was
        // N × the SNMP timeout — a scan that found 30 printers could sit for over a
        // minute looking hung. Task.WhenAll preserves order, so the list stays sorted.
        var ordered = found.Distinct().OrderBy(LastOctet).ToList();
        using var enrichGate = new SemaphoreSlim(16);
        var enriched = await Task.WhenAll(ordered.Select(ip => EnrichAsync(ip, enrichGate, cancellationToken)));
        return enriched.ToList();
    }

    private async Task<DiscoveredNetworkPrinter> EnrichAsync(string ip, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        var printer = new DiscoveredNetworkPrinter { IpAddress = ip };
        await gate.WaitAsync(cancellationToken);
        try
        {
            var hardware = await snmpQueryService.GetHardwareAsync(ip, cancellationToken);
            if (hardware is { Success: true, Value: not null })
            {
                printer.Model = hardware.Value.Model;
                printer.Name = hardware.Value.Model;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // SNMP enrichment is best-effort.
        }
        finally
        {
            gate.Release();
        }

        return printer;
    }

    private async Task ProbeAsync(string ip, ConcurrentBag<string> found, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var probe = await networkProbeProvider.CheckTcpPortAsync(ip, RawPrintPort, ProbeTimeoutMs, cancellationToken);
            if (probe.Success)
            {
                found.Add(ip);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Unreachable host — ignore.
        }
        finally
        {
            gate.Release();
        }
    }

    private static IEnumerable<string> GetLocalSubnetPrefixes()
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var bytes = address.Address.GetAddressBytes();

                // Skip APIPA / link-local (169.254/16): no real printers live there,
                // and a disconnected NIC would otherwise waste 254 probes.
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    continue;
                }

                prefixes.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.");
            }
        }

        return prefixes;
    }

    private static int LastOctet(string ip) =>
        int.TryParse(ip.Split('.').Last(), out var octet) ? octet : 0;
}
