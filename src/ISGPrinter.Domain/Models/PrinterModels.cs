using ISGPrinter.Domain.Enums;

namespace ISGPrinter.Domain.Models;

public sealed class PrintServer
{
    public string Name { get; set; } = string.Empty;

    public string Fqdn { get; set; } = string.Empty;

    public bool IsReachable { get; set; }

    public bool WasDiscoveredFromActiveDirectory { get; set; }

    public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.MinValue;
}

public sealed class PrinterDevice
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ShareName { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string UncPath { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DriverName { get; set; } = string.Empty;

    public string PortName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public bool IsInstalledLocally { get; set; }

    public bool IsDefault { get; set; }

    public PrinterOnlineState OnlineState { get; set; } = PrinterOnlineState.Unknown;

    public TonerStatus TonerStatus { get; set; } = new();

    public PrinterHardwareInfo HardwareInfo { get; set; } = new();
}

public sealed class LocalPrinter
{
    public string Name { get; set; } = string.Empty;

    public string DriverName { get; set; } = string.Empty;

    public string PortName { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string UncPath { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool WorkOffline { get; set; }

    public string Status { get; set; } = string.Empty;

    public string QueueStatus { get; set; } = string.Empty;

    public PrinterConnectionType ConnectionType { get; set; } = PrinterConnectionType.Unknown;
}

public sealed class PrintEventEntry
{
    public DateTimeOffset Time { get; set; }

    public int Id { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class DiscoveredNetworkPrinter
{
    public string IpAddress { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

public sealed class PrintJob
{
    public int Id { get; set; }

    public string Document { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public int? TotalPages { get; set; }

    public int? PagesPrinted { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? SubmittedAt { get; set; }
}

public sealed class TonerStatus
{
    public int? BlackPercent { get; set; }

    public int? CyanPercent { get; set; }

    public int? MagentaPercent { get; set; }

    public int? YellowPercent { get; set; }

    public TonerLevelState State { get; set; } = TonerLevelState.Unknown;

    public string RawStatus { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }
}

public sealed class PrinterHardwareInfo
{
    public string Model { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public long? PageCounter { get; set; }

    public string FirmwareVersion { get; set; } = string.Empty;
}
