using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ISGPrinter.Application.Abstractions;

namespace ISGPrinter.App.ViewModels;

public sealed partial class AboutViewModel(
    IAppEnvironmentService environmentService,
    ISettingsService settingsService) : ObservableObject
{
    public string AppName { get; } = "ISG Printer";

    public string Version { get; } = "0.1.0";

    public string Description { get; } =
        "ISG Printer is a technician-only Windows desktop tool for IT support, helpdesk and " +
        "system administrators. It runs elevated and performs every action under the current " +
        "Windows account. It never repairs silently — it gives the technician clear diagnostics, " +
        "device data and recommended steps, so the person stays in control.";

    public IReadOnlyList<string> Features { get; } =
    [
        "Discover company printers from print servers (manual or auto-detect via installed printers and Active Directory).",
        "Manage local printers: live print queue, clear stuck jobs, restart the spooler, print a test page, remove a printer.",
        "Layered diagnostics: spooler, install, driver, network reachability, Windows print errors and SNMP supplies — with a clear verdict.",
        "Read toner, model, serial and page count over SNMP v2c / v3 (credentials configured for the current session).",
        "Ticket-ready reports you can copy or save to a file."
    ];

    public string Author { get; } = "Ionel-Silviu Ghimpău";

    public string Copyright { get; } = $"© {DateTime.Now:yyyy} ISG Printer";

    public string BuildDate { get; } = ResolveBuildDate();

    public string Platform { get; } = ".NET 10 LTS + WPF";

    public string SettingsPath => settingsService.SettingsPath;

    public string LogsPath { get; } = Path.Combine(AppContext.BaseDirectory, "Logs");

    public string StorageMode { get; } = "Portable - settings and SNMP credentials live only in memory; logs stay next to the executable.";

    [ObservableProperty]
    private string computerName = "-";

    [ObservableProperty]
    private string userName = "-";

    [ObservableProperty]
    private string domainName = "-";

    [ObservableProperty]
    private string windowsVersion = "-";

    [ObservableProperty]
    private bool isElevated;

    [ObservableProperty]
    private string statusText = string.Empty;

    public async Task LoadAsync()
    {
        var environment = await environmentService.GetEnvironmentAsync(CancellationToken.None);
        ComputerName = environment.ComputerName;
        UserName = environment.UserName;
        DomainName = environment.DomainName;
        WindowsVersion = environment.WindowsVersion;
        IsElevated = environment.IsElevated;
    }

    [RelayCommand]
    private void CopyEnvironmentInfo()
    {
        var text = string.Join(Environment.NewLine,
            $"{AppName} {Version}",
            $"Computer : {ComputerName}",
            $"User     : {UserName}",
            $"Domain   : {DomainName}",
            $"Windows  : {WindowsVersion}",
            $"Elevated : {(IsElevated ? "Yes" : "No")}",
            $"Captured : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        Clipboard.SetText(text);
        StatusText = "Environment info copied to clipboard.";
    }

    private static string ResolveBuildDate()
    {
        try
        {
            // Environment.ProcessPath resolves the real executable even in a
            // single-file publish, where Assembly.Location is empty.
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
            }
        }
        catch
        {
            // Fall through.
        }

        return "-";
    }
}
