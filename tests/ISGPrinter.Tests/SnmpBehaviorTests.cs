using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;
using ISGPrinter.Infrastructure.Security;
using ISGPrinter.Infrastructure.Snmp;

namespace ISGPrinter.Tests;

public sealed class SnmpBehaviorTests
{
    [Fact]
    public async Task SnmpQueryService_UsesConfiguredTimeout()
    {
        var settings = new AppSettings { SnmpTimeoutMs = 7200 };
        var provider = new CapturingSnmpProvider();
        var service = new SnmpQueryService(new FakeSettingsService(settings), provider);

        await service.GetStatusAsync("192.0.2.10", CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(7200), provider.LastTimeout);
    }

    [Fact]
    public async Task SnmpQueryService_ClampsTimeoutToProductionBounds()
    {
        var settings = new AppSettings { SnmpTimeoutMs = 120000 };
        var provider = new CapturingSnmpProvider();
        var service = new SnmpQueryService(new FakeSettingsService(settings), provider);

        await service.GetStatusAsync("192.0.2.10", CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(15000), provider.LastTimeout);
    }

    [Fact]
    public async Task SnmpPrinterProvider_WithoutV2Community_ReturnsSessionConfigurationMessage()
    {
        var provider = new SnmpPrinterProvider(new InMemorySecretStore());

        var result = await provider.GetRawPrinterStatusAsync(
            "192.0.2.10", // RFC 5737 TEST-NET — never answers, so the query fails on the network.
            SnmpProfile.DefaultV2(),
            TimeSpan.FromMilliseconds(500),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SNMP v2c community is not configured for this session.", result.Message);
    }

    private sealed class CapturingSnmpProvider : ISnmpPrinterProvider
    {
        public TimeSpan LastTimeout { get; private set; }

        public Task<SnmpResult<TonerStatus>> GetTonerStatusAsync(
            string ipAddress,
            SnmpProfile profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;
            return Task.FromResult(new SnmpResult<TonerStatus> { Success = true, Value = new TonerStatus() });
        }

        public Task<SnmpResult<PrinterHardwareInfo>> GetHardwareInfoAsync(
            string ipAddress,
            SnmpProfile profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;
            return Task.FromResult(new SnmpResult<PrinterHardwareInfo> { Success = true, Value = new PrinterHardwareInfo() });
        }

        public Task<SnmpResult<long?>> GetPageCounterAsync(
            string ipAddress,
            SnmpProfile profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;
            return Task.FromResult(new SnmpResult<long?> { Success = true, Value = 1 });
        }

        public Task<SnmpResult<string>> GetRawPrinterStatusAsync(
            string ipAddress,
            SnmpProfile profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;
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
