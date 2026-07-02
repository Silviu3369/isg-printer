using System.Management;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class WmiLocalPrinterService : ILocalPrinterService
{
    private readonly object enumerationGate = new();
    private Task<IReadOnlyList<LocalPrinter>>? inFlightEnumeration;

    public async Task<IReadOnlyList<LocalPrinter>> GetLocalPrintersAsync(CancellationToken cancellationToken)
    {
        // Coalesce the startup burst: shell status, Local Printers, Reports,
        // Diagnostics and discovery all enumerate within milliseconds of each
        // other. Share one WMI sweep instead of running it ~4× in parallel.
        // Once the in-flight sweep finishes, the next caller starts a fresh one,
        // so no caller ever sees stale data.
        Task<IReadOnlyList<LocalPrinter>> sweep;
        lock (enumerationGate)
        {
            if (inFlightEnumeration is not { IsCompleted: false })
            {
                inFlightEnumeration = EnumerateAsync();
            }

            sweep = inFlightEnumeration;
        }

        // Honour the caller's cancellation without cancelling the shared sweep
        // for the other callers awaiting the same result.
        return await sweep.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task<IReadOnlyList<LocalPrinter>> EnumerateAsync() =>
        // WMI enumeration is blocking; keep it off the UI thread.
        Task.Run<IReadOnlyList<LocalPrinter>>(() =>
        {
            var printers = new List<LocalPrinter>();
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");

            foreach (var printer in searcher.Get().Cast<ManagementObject>())
            {
                using (printer)
                {
                    printers.Add(MapPrinter(printer));
                }
            }

            return printers.OrderByDescending(printer => printer.IsDefault).ThenBy(printer => printer.Name).ToList();
        });

    public async Task<LocalPrinter?> GetDefaultPrinterAsync(CancellationToken cancellationToken)
    {
        var printers = await GetLocalPrintersAsync(cancellationToken);
        return printers.FirstOrDefault(printer => printer.IsDefault);
    }

    public async Task<bool> IsPrinterInstalledAsync(string printerNameOrUncPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerNameOrUncPath))
        {
            return false;
        }

        var printers = await GetLocalPrintersAsync(cancellationToken);
        return printers.Any(printer =>
            string.Equals(printer.Name, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(printer.UncPath, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase));
    }

    public Task<string?> GetPortHostAddressAsync(string portName, CancellationToken cancellationToken) =>
        Task.Run<string?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(portName))
            {
                return null;
            }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT HostAddress FROM Win32_TCPIPPrinterPort WHERE Name = '{EscapeWql(portName)}'");
                using var port = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                var host = port is null ? null : GetString(port, "HostAddress");
                return string.IsNullOrWhiteSpace(host) ? null : host;
            }
            catch
            {
                return null;
            }
        }, cancellationToken);

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static LocalPrinter MapPrinter(ManagementBaseObject printer)
    {
        var name = GetString(printer, "Name");
        var serverName = GetString(printer, "ServerName").TrimStart('\\');
        var shareName = GetString(printer, "ShareName");
        var portName = GetString(printer, "PortName");
        var isNetwork = GetBool(printer, "Network");
        var uncPath = name.StartsWith(@"\\", StringComparison.Ordinal) ? name : BuildUnc(serverName, shareName);

        var workOffline = GetBool(printer, "WorkOffline");

        return new LocalPrinter
        {
            Name = name,
            DriverName = GetString(printer, "DriverName"),
            PortName = portName,
            ServerName = serverName,
            UncPath = uncPath,
            IsDefault = GetBool(printer, "Default"),
            WorkOffline = workOffline,
            Status = DescribePrinterStatus(GetUInt(printer, "PrinterStatus"), workOffline),
            QueueStatus = DescribeExtendedStatus(GetUInt(printer, "ExtendedPrinterStatus")),
            ConnectionType = ResolveConnectionType(isNetwork, portName, name)
        };
    }

    // Win32_Printer.PrinterStatus is a numeric code; map it to readable text.
    private static string DescribePrinterStatus(uint? status, bool workOffline)
    {
        if (workOffline)
        {
            return "Offline (use printer offline)";
        }

        return status switch
        {
            1 => "Other",
            3 => "Ready",
            4 => "Printing",
            5 => "Warming up",
            6 => "Stopped",
            7 => "Offline",
            _ => "Unknown"
        };
    }

    // Win32_Printer.ExtendedPrinterStatus carries the richer queue state.
    private static string DescribeExtendedStatus(uint? status) =>
        status switch
        {
            1 => "Other",
            3 => "Idle",
            4 => "Printing",
            5 => "Warming up",
            6 => "Stopped",
            7 => "Offline",
            8 => "Paused",
            9 => "Error",
            10 => "Busy",
            11 => "Not available",
            12 => "Waiting",
            13 => "Processing",
            14 => "Initializing",
            15 => "Power save",
            16 => "Pending deletion",
            17 => "I/O active",
            18 => "Manual feed",
            _ => "Unknown"
        };

    private static PrinterConnectionType ResolveConnectionType(bool isNetwork, string portName, string name)
    {
        if (isNetwork || name.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return PrinterConnectionType.Network;
        }

        if (portName.StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase) ||
            portName.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterConnectionType.Virtual;
        }

        return PrinterConnectionType.Local;
    }

    private static string BuildUnc(string serverName, string shareName)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(shareName))
        {
            return string.Empty;
        }

        return $@"\\{serverName}\{shareName}";
    }

    private static string GetString(ManagementBaseObject printer, string propertyName) =>
        printer.Properties[propertyName]?.Value?.ToString() ?? string.Empty;

    private static bool GetBool(ManagementBaseObject printer, string propertyName) =>
        printer.Properties[propertyName]?.Value is bool value && value;

    private static uint? GetUInt(ManagementBaseObject printer, string propertyName)
    {
        var value = printer.Properties[propertyName]?.Value;
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt32(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
