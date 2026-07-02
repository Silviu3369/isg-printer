using ISGPrinter.Application.Abstractions;
using ISGPrinter.Infrastructure.Diagnostics;
using ISGPrinter.Infrastructure.Printers;
using ISGPrinter.Infrastructure.Reports;
using ISGPrinter.Infrastructure.Security;
using ISGPrinter.Infrastructure.Settings;
using ISGPrinter.Infrastructure.Snmp;
using ISGPrinter.Infrastructure.SystemInfo;
using Microsoft.Extensions.DependencyInjection;

namespace ISGPrinter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppEnvironmentService, WindowsAppEnvironmentService>();

        // Portable mode: settings and secrets live only in memory (no disk
        // footprint, fresh every launch). Swap these two back to
        // FileSettingsService / FileSecretStore to restore persistence.
        services.AddSingleton<ISettingsService, InMemorySettingsService>();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();

        services.AddSingleton<ILocalPrinterService, WmiLocalPrinterService>();
        services.AddSingleton<IPrinterDiscoveryService, PrinterDiscoveryService>();
        services.AddSingleton<IPrinterDetailsService, PrinterDetailsService>();
        services.AddSingleton<IPrinterInstallService, PrinterInstallService>();
        services.AddSingleton<IDefaultPrinterService, DefaultPrinterService>();
        services.AddSingleton<IPrinterActionsService, WmiPrinterActionsService>();
        services.AddSingleton<IPrintServerAutodetectService, PrintServerAutodetectService>();
        services.AddSingleton<INetworkPrinterScanner, NetworkPrinterScanner>();

        services.AddSingleton<INetworkProbeProvider, NetworkProbeProvider>();
        services.AddSingleton<ISpoolerServiceProvider, SpoolerServiceProvider>();
        services.AddSingleton<IPrintEventLogProvider, WindowsPrintEventLogProvider>();
        services.AddSingleton<IPrinterDiagnosticsService, PrinterDiagnosticsService>();
        services.AddSingleton<ITechnicianGuidanceService, TechnicianGuidanceService>();

        services.AddSingleton<ISnmpPrinterProvider, SnmpPrinterProvider>();
        services.AddSingleton<ISnmpQueryService, SnmpQueryService>();
        services.AddSingleton<IPrinterReportService, PrinterReportService>();

        return services;
    }
}
