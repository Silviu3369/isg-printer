namespace ISGPrinter.Domain.Models;

public sealed class AppSettings
{
    public string AppName { get; set; } = "ISG Printer";

    public string Version { get; set; } = "0.1.0";

    public List<string> KnownPrintServers { get; set; } = [];

    public bool EnableActiveDirectoryDiscovery { get; set; } = true;

    public bool EnableSnmp { get; set; } = true;

    public string DefaultSnmpProfile { get; set; } = "Default";

    public List<int> TcpPortsToCheck { get; set; } = [9100, 631];

    public bool EnableIpRangeScan { get; set; }

    public string DefaultExportFolder { get; set; } = "%USERPROFILE%\\Documents\\ISG Printer Reports";

    public string Theme { get; set; } = "System";

    public int NetworkTimeoutMs { get; set; } = 2000;

    public int SnmpTimeoutMs { get; set; } = 2500;

    public int LogRetentionDays { get; set; } = 14;

    public int CacheDurationMinutes { get; set; } = 10;

    public List<SnmpProfile> SnmpProfiles { get; set; } = [SnmpProfile.DefaultV2()];
}
