using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IAppEnvironmentService environmentService;
    private readonly ISettingsService settingsService;

    public ShellViewModel(
        IAppEnvironmentService environmentService,
        ISettingsService settingsService,
        PrintersViewModel printers,
        LocalPrintersViewModel localPrinters,
        DiagnosticsViewModel diagnostics,
        ReportsViewModel reports,
        SettingsViewModel settings,
        AboutViewModel about)
    {
        this.environmentService = environmentService;
        this.settingsService = settingsService;
        Printers = printers;
        LocalPrinters = localPrinters;
        Diagnostics = diagnostics;
        Reports = reports;
        Settings = settings;
        About = about;

        NavigationItems =
        [
            new("Printers", "", "#1C6FD0", Printers),
            new("Local Printers", "", "#0C857A", LocalPrinters),
            new("Diagnostics", "", "#BB5512", Diagnostics),
            new("Reports", "", "#7C3AED", Reports),
            new("Settings", "", "#15823C", Settings),
            new("About", "", "#4A63C4", About)
        ];

        NavigationItems[0].IsSelected = true;
        CurrentPage = Printers;
    }

    public PrintersViewModel Printers { get; }

    public LocalPrintersViewModel LocalPrinters { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public ReportsViewModel Reports { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private object? currentPage;

    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        Printers.FilterText = value;
        LocalPrinters.FilterText = value;
    }

    [ObservableProperty]
    private string computerName = "-";

    [ObservableProperty]
    private string runningUser = "-";

    [ObservableProperty]
    private string domainName = "-";

    [ObservableProperty]
    private bool isElevated;

    [ObservableProperty]
    private string defaultPrinter = "Default printer: -";

    [ObservableProperty]
    private string knownServers = "Known servers: 0";

    [ObservableProperty]
    private string lastRefresh = "Last refresh: -";

    [ObservableProperty]
    private string activeSnmpProfile = "SNMP profile: -";

    [ObservableProperty]
    private string warningText = string.Empty;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        // Auto-sync: when any module changes printer state, reload the grids.
        WeakReferenceMessenger.Default.Register<PrintersChangedMessage>(
            this, (recipient, _) => ((ShellViewModel)recipient).OnPrintersChanged());
        WeakReferenceMessenger.Default.Register<SessionSettingsChangedMessage>(
            this, (recipient, _) => ((ShellViewModel)recipient).OnSessionSettingsChanged());

        // Settings first (the rest read it); then load everything in parallel
        // so a slow print-server query never blocks the whole startup.
        await Settings.LoadAsync();
        await Task.WhenAll(
            RefreshShellStatusAsync(CancellationToken.None),
            LocalPrinters.LoadAsync(),
            Printers.LoadAsync(),
            Diagnostics.LoadAsync(),
            Reports.LoadAsync(),
            About.LoadAsync());
    }

    [RelayCommand]
    private void Navigate(NavigationItemViewModel item)
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = false;
        }

        item.IsSelected = true;
        CurrentPage = item.PageViewModel;
    }

    [RelayCommand]
    private Task RefreshAllAsync() =>
        Task.WhenAll(
            RefreshShellStatusAsync(CancellationToken.None),
            LocalPrinters.LoadAsync(),
            Printers.LoadAsync(),
            Diagnostics.LoadAsync(),
            Reports.LoadAsync(),
            About.LoadAsync());

    private void OnPrintersChanged() => _ = RefreshAfterChangeAsync();

    private void OnSessionSettingsChanged() => _ = RefreshShellStatusAsync(CancellationToken.None);

    private Task RefreshAfterChangeAsync() =>
        Task.WhenAll(
            LocalPrinters.LoadAsync(),
            Printers.LoadAsync(),
            RefreshShellStatusAsync(CancellationToken.None));

    private async Task RefreshShellStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppEnvironmentInfo environment = await environmentService.GetEnvironmentAsync(cancellationToken);
            AppSettings settings = await settingsService.LoadAsync(cancellationToken);
            LocalPrinter? defaultLocalPrinter = await LocalPrinters.GetDefaultPrinterAsync(cancellationToken);

            ComputerName = environment.ComputerName;
            RunningUser = environment.UserName;
            DomainName = environment.DomainName;
            IsElevated = environment.IsElevated;
            WarningText = environment.IsElevated ? string.Empty : "Application is not elevated.";
            DefaultPrinter = $"Default printer: {defaultLocalPrinter?.Name ?? "-"}";
            KnownServers = $"Known servers: {settings.KnownPrintServers.Count}";
            ActiveSnmpProfile = $"SNMP profile: {settings.DefaultSnmpProfile}";
            LastRefresh = $"Last refresh: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            // Shell status is best-effort: a WMI / environment hiccup must never
            // bubble out of a parallel startup load or a fire-and-forget refresh
            // and take the app down. Keep last-known values and note it.
            Serilog.Log.Warning(ex, "Shell status refresh failed.");
            LastRefresh = $"Last refresh: {DateTime.Now:HH:mm:ss} (partial)";
        }
    }
}
