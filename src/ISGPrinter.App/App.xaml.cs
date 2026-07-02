using System.IO;
using System.Windows;
using System.Windows.Threading;
using ISGPrinter.App.ViewModels;
using ISGPrinter.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ISGPrinter.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();

        // Production safety net: log and surface any unhandled error instead
        // of letting the process die silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        services.AddSingleton<PrintersViewModel>();
        services.AddSingleton<LocalPrintersViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();

        Log.Information("ISG Printer starting.");

        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("ISG Printer exiting.");
        Log.CloseAndFlush();
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe action was cancelled, but ISG Printer will keep running.",
            "ISG Printer",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Error(exception, "Unhandled non-UI exception.");
        }

        Log.CloseAndFlush();
    }

    // Fire-and-forget tasks (auto-refresh, SNMP supply reads) that fault must not
    // go silent — log them and mark observed so they never escalate.
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }

    private static void ConfigureLogging()
    {
        // Portable mode: logs stay next to the executable (for USB usage) and
        // never store SNMP communities or passwords. If the location is not
        // writable, logging becomes a no-op instead of blocking startup.
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "isg-printer-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 5_000_000,
                    rollOnFileSizeLimit: true,
                    shared: true)
                .CreateLogger();
        }
        catch
        {
            Log.Logger = new LoggerConfiguration().CreateLogger();
        }
    }
}
