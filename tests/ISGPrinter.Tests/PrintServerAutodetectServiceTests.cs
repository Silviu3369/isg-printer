using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;
using ISGPrinter.Infrastructure.Printers;

namespace ISGPrinter.Tests;

public sealed class PrintServerAutodetectServiceTests
{
    [Fact]
    public async Task DetectServersAsync_ReturnsNormalizedServersFromInstalledPrinters()
    {
        var service = new PrintServerAutodetectService(
            new FakeLocalPrinterService(
            [
                new LocalPrinter { Name = @"\\PRINT02\Color", UncPath = @"\\PRINT02\Color" },
                new LocalPrinter { Name = "Reception", ServerName = @"\\PRINT01" },
                new LocalPrinter { Name = "Duplicate", UncPath = @"\\print02\Mono" }
            ]),
            new FakeEnvironmentService(new AppEnvironmentInfo { IsDomainJoined = false }),
            new FakeSettingsService(new AppSettings()));

        var servers = await service.DetectServersAsync(CancellationToken.None);

        Assert.Equal(["PRINT01", "PRINT02"], servers);
    }

    [Fact(Timeout = 1000)]
    public async Task DetectServersAsync_WhenActiveDirectoryDiscoveryIsDisabled_DoesNotRequireDomainLookup()
    {
        var settings = new AppSettings { EnableActiveDirectoryDiscovery = false };
        var environment = new AppEnvironmentInfo
        {
            IsDomainJoined = true,
            DomainName = "invalid-domain-for-unit-test.example"
        };

        var service = new PrintServerAutodetectService(
            new FakeLocalPrinterService([]),
            new FakeEnvironmentService(environment),
            new FakeSettingsService(settings));

        var servers = await service.DetectServersAsync(CancellationToken.None);

        Assert.Empty(servers);
    }

    private sealed class FakeLocalPrinterService(IReadOnlyList<LocalPrinter> printers) : ILocalPrinterService
    {
        public Task<IReadOnlyList<LocalPrinter>> GetLocalPrintersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(printers);

        public Task<LocalPrinter?> GetDefaultPrinterAsync(CancellationToken cancellationToken) =>
            Task.FromResult(printers.FirstOrDefault(printer => printer.IsDefault));

        public Task<bool> IsPrinterInstalledAsync(string printerNameOrUncPath, CancellationToken cancellationToken) =>
            Task.FromResult(printers.Any(printer =>
                string.Equals(printer.Name, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(printer.UncPath, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase)));

        public Task<string?> GetPortHostAddressAsync(string portName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeEnvironmentService(AppEnvironmentInfo environment) : IAppEnvironmentService
    {
        public Task<AppEnvironmentInfo> GetEnvironmentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(environment);
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
