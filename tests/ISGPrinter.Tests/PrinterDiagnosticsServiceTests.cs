using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;
using ISGPrinter.Infrastructure.Diagnostics;

namespace ISGPrinter.Tests;

public sealed class PrinterDiagnosticsServiceTests
{
    [Fact]
    public async Task DiagnoseAsync_UncOnlySharedPrinter_DoesNotProbeRawPortsOnPrintServer()
    {
        var network = new RecordingNetworkProbeProvider();
        var snmp = new RecordingSnmpQueryService { Enabled = true };
        var service = CreateService(network, snmp);

        var result = await service.DiagnoseAsync(
            new PrinterDevice { Name = "HP Accounting", UncPath = @"\\PRINT01\HP-ACCOUNTING" },
            CancellationToken.None);

        Assert.Empty(network.PortChecks);
        Assert.Empty(snmp.TonerTargets);
        Assert.Empty(snmp.StatusTargets);
        Assert.Contains(result.Checks, check => check is { Name: "Print server", Status: DiagnosticStatus.Ok });
        Assert.Contains(result.Checks, check => check is { Name: "Printer device", Status: DiagnosticStatus.Unknown });
        Assert.DoesNotContain(result.Checks, check => check.Name == "Print ports");
    }

    [Fact]
    public async Task DiagnoseAsync_DeviceIp_ProbesDevicePortsAndSnmpOnDeviceTarget()
    {
        var network = new RecordingNetworkProbeProvider();
        var snmp = new RecordingSnmpQueryService { Enabled = true };
        var service = CreateService(network, snmp);

        var result = await service.DiagnoseAsync(
            new PrinterDevice { Name = "HP Lobby", IpAddress = "10.10.1.20" },
            CancellationToken.None);

        Assert.Equal(["10.10.1.20:9100", "10.10.1.20:631"], network.PortChecks);
        Assert.Equal(["10.10.1.20"], snmp.TonerTargets);
        Assert.Equal(["10.10.1.20"], snmp.StatusTargets);
        Assert.Contains(result.Checks, check => check.Name == "Print ports");
    }

    private static PrinterDiagnosticsService CreateService(
        RecordingNetworkProbeProvider network,
        RecordingSnmpQueryService snmp)
    {
        var settings = new AppSettings
        {
            NetworkTimeoutMs = 50,
            TcpPortsToCheck = [9100, 631],
            EnableSnmp = true
        };

        return new PrinterDiagnosticsService(
            new FakeEnvironmentService(),
            new FakeLocalPrinterService(),
            new FakeSpoolerServiceProvider(),
            network,
            new FakePrintEventLogProvider(),
            snmp,
            new FakeSettingsService(settings),
            new TechnicianGuidanceService());
    }

    private sealed class FakeEnvironmentService : IAppEnvironmentService
    {
        public Task<AppEnvironmentInfo> GetEnvironmentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppEnvironmentInfo { IsElevated = true });
    }

    private sealed class FakeLocalPrinterService : ILocalPrinterService
    {
        public Task<IReadOnlyList<LocalPrinter>> GetLocalPrintersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalPrinter>>([]);

        public Task<LocalPrinter?> GetDefaultPrinterAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LocalPrinter?>(null);

        public Task<bool> IsPrinterInstalledAsync(string printerNameOrUncPath, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<string?> GetPortHostAddressAsync(string portName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeSpoolerServiceProvider : ISpoolerServiceProvider
    {
        public Task<PrinterDiagnosticCheck> CheckSpoolerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PrinterDiagnosticCheck
            {
                Name = "Print Spooler",
                Status = DiagnosticStatus.Ok,
                Message = "Print Spooler is running."
            });

        public Task<OperationResult> RestartSpoolerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok());
    }

    private sealed class RecordingNetworkProbeProvider : INetworkProbeProvider
    {
        public List<string> PortChecks { get; } = [];

        public Task<NetworkProbeResult> ResolveDnsAsync(string hostName, CancellationToken cancellationToken) =>
            Task.FromResult(new NetworkProbeResult
            {
                Success = true,
                Target = hostName,
                Message = "DNS resolved."
            });

        public Task<NetworkProbeResult> PingAsync(string hostNameOrIpAddress, int timeoutMs, CancellationToken cancellationToken) =>
            Task.FromResult(new NetworkProbeResult
            {
                Success = true,
                Target = hostNameOrIpAddress,
                Elapsed = TimeSpan.FromMilliseconds(1),
                Message = "Ping succeeded."
            });

        public Task<NetworkProbeResult> CheckTcpPortAsync(string hostNameOrIpAddress, int port, int timeoutMs, CancellationToken cancellationToken)
        {
            PortChecks.Add($"{hostNameOrIpAddress}:{port}");
            return Task.FromResult(new NetworkProbeResult
            {
                Success = port == 9100,
                Target = hostNameOrIpAddress,
                Port = port,
                Message = port == 9100 ? "TCP reachable." : "TCP closed."
            });
        }
    }

    private sealed class FakePrintEventLogProvider : IPrintEventLogProvider
    {
        public Task<IReadOnlyList<PrintEventEntry>> GetRecentPrintErrorsAsync(
            TimeSpan window,
            int maxEntries,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrintEventEntry>>([]);
    }

    private sealed class RecordingSnmpQueryService : ISnmpQueryService
    {
        public bool Enabled { get; init; }

        public List<string> TonerTargets { get; } = [];

        public List<string> StatusTargets { get; } = [];

        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Enabled);

        public Task<SnmpResult<TonerStatus>> GetTonerAsync(string ipAddress, CancellationToken cancellationToken)
        {
            TonerTargets.Add(ipAddress);
            return Task.FromResult(new SnmpResult<TonerStatus>
            {
                Success = true,
                Value = new TonerStatus { IsAvailable = true, State = TonerLevelState.Ok, BlackPercent = 75 }
            });
        }

        public Task<SnmpResult<PrinterHardwareInfo>> GetHardwareAsync(string ipAddress, CancellationToken cancellationToken) =>
            Task.FromResult(SnmpResult<PrinterHardwareInfo>.Unavailable("Not used."));

        public Task<SnmpResult<long?>> GetPageCountAsync(string ipAddress, CancellationToken cancellationToken) =>
            Task.FromResult(SnmpResult<long?>.Unavailable("Not used."));

        public Task<SnmpResult<string>> GetStatusAsync(string ipAddress, CancellationToken cancellationToken)
        {
            StatusTargets.Add(ipAddress);
            return Task.FromResult(new SnmpResult<string> { Success = true, Value = "Idle" });
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public string SettingsPath => "test";

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(settings);

        public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok());
    }
}
