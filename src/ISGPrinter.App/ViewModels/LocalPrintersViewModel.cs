using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.App.ViewModels;

public sealed partial class LocalPrintersViewModel(
    ILocalPrinterService localPrinterService,
    IDefaultPrinterService defaultPrinterService,
    IPrinterActionsService printerActionsService,
    ISpoolerServiceProvider spoolerServiceProvider,
    ISnmpQueryService snmpQueryService) : ObservableObject
{
    private ICollectionView? printersView;
    private CancellationTokenSource? jobsCts;
    private CancellationTokenSource? suppliesCts;

    public ObservableCollection<LocalPrinter> Printers { get; } = [];

    public ObservableCollection<PrintJob> Jobs { get; } = [];

    public ICollectionView PrintersView
    {
        get
        {
            if (printersView is null)
            {
                printersView = CollectionViewSource.GetDefaultView(Printers);
                printersView.Filter = MatchesFilter;
            }

            return printersView;
        }
    }

    [ObservableProperty]
    private string filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => PrintersView.Refresh();

    private bool MatchesFilter(object item)
    {
        if (string.IsNullOrWhiteSpace(FilterText) || item is not LocalPrinter printer)
        {
            return true;
        }

        return Matches(printer.Name)
            || Matches(printer.UncPath)
            || Matches(printer.ServerName)
            || Matches(printer.PortName)
            || Matches(printer.DriverName);

        bool Matches(string value) => value.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private LocalPrinter? selectedPrinter;

    partial void OnSelectedPrinterChanged(LocalPrinter? value)
    {
        _ = LoadJobsAsync();
        StartSuppliesRead(value);
    }

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasLiveData;

    [ObservableProperty]
    private bool isReadingSupplies;

    [ObservableProperty]
    private string liveToner = string.Empty;

    [ObservableProperty]
    private string liveModel = string.Empty;

    [ObservableProperty]
    private string liveSerial = string.Empty;

    [ObservableProperty]
    private string livePageCount = string.Empty;

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Reading installed printers…";

        try
        {
            Printers.Clear();
            var printers = await localPrinterService.GetLocalPrintersAsync(CancellationToken.None);
            foreach (var printer in printers)
            {
                Printers.Add(printer);
            }

            StatusText = Printers.Count == 0
                ? "No printers are installed for this account yet."
                : $"{Printers.Count} printer(s) installed for the current account.";
        }
        catch (Exception ex)
        {
            StatusText = $"Local printer read failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task<LocalPrinter?> GetDefaultPrinterAsync(CancellationToken cancellationToken) =>
        localPrinterService.GetDefaultPrinterAsync(cancellationToken);

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task SetDefaultSelectedAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a local printer first.";
            return;
        }

        var result = await defaultPrinterService.SetDefaultPrinterAsync(SelectedPrinter.Name, CancellationToken.None);
        StatusText = result.Message;
        WeakReferenceMessenger.Default.Send(new PrintersChangedMessage());
    }

    private async Task LoadJobsAsync()
    {
        jobsCts?.Cancel();
        jobsCts?.Dispose();
        jobsCts = new CancellationTokenSource();
        var token = jobsCts.Token;

        Jobs.Clear();

        var printer = SelectedPrinter;
        if (printer is null)
        {
            return;
        }

        try
        {
            var jobs = await printerActionsService.GetJobsAsync(printer.Name, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var job in jobs)
            {
                Jobs.Add(job);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection/refresh.
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read the print queue: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task RefreshJobsAsync() => LoadJobsAsync();

    private void StartSuppliesRead(LocalPrinter? printer)
    {
        HasLiveData = false;
        LiveToner = string.Empty;
        LiveModel = string.Empty;
        LiveSerial = string.Empty;
        LivePageCount = string.Empty;

        suppliesCts?.Cancel();

        // Auto-read supplies for printers on a TCP/IP port. If the session has
        // no SNMP credentials, the read reports that clearly.
        if (printer is not null && !string.IsNullOrWhiteSpace(printer.PortName))
        {
            suppliesCts = new CancellationTokenSource();
            _ = ReadSuppliesCoreAsync(printer, suppliesCts.Token);
        }
    }

    [RelayCommand]
    private async Task ReadSuppliesAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        suppliesCts?.Cancel();
        suppliesCts = new CancellationTokenSource();
        await ReadSuppliesCoreAsync(SelectedPrinter, suppliesCts.Token);
    }

    private async Task ReadSuppliesCoreAsync(LocalPrinter printer, CancellationToken token)
    {
        IsReadingSupplies = true;

        try
        {
            // A local printer carries a port, not an IP — resolve the TCP/IP host
            // address first. Non-IP ports (USB, WSD, a server share) have none.
            var ip = await localPrinterService.GetPortHostAddressAsync(printer.PortName, token);
            if (token.IsCancellationRequested || !ReferenceEquals(printer, SelectedPrinter))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                LiveToner = "No TCP/IP address on this printer's port — SNMP not available.";
                HasLiveData = true;
                return;
            }

            StatusText = $"Reading {printer.Name} over SNMP…";

            // Independent queries — run them together so the read takes about one
            // timeout, not three.
            var tonerTask = snmpQueryService.GetTonerAsync(ip, token);
            var hardwareTask = snmpQueryService.GetHardwareAsync(ip, token);
            var pageTask = snmpQueryService.GetPageCountAsync(ip, token);
            await Task.WhenAll(tonerTask, hardwareTask, pageTask);

            // A newer selection/read superseded this one — drop the stale result.
            if (token.IsCancellationRequested || !ReferenceEquals(printer, SelectedPrinter))
            {
                return;
            }

            var toner = await tonerTask;
            var hardware = await hardwareTask;
            var pages = await pageTask;

            LiveToner = toner is { Success: true, Value: { IsAvailable: true } supplies }
                ? DescribeToner(supplies)
                : toner.Message;

            if (hardware is { Success: true, Value: not null })
            {
                LiveModel = hardware.Value.Model;
                LiveSerial = hardware.Value.SerialNumber;
            }

            LivePageCount = pages.Success && pages.Value.HasValue ? pages.Value.Value.ToString("N0") : string.Empty;

            HasLiveData = true;
            StatusText = toner.Success
                ? $"SNMP read complete for {printer.Name}."
                : $"SNMP: {toner.Message}";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection/read.
        }
        catch (Exception ex)
        {
            StatusText = $"SNMP read failed: {ex.Message}";
        }
        finally
        {
            if (suppliesCts is not null && suppliesCts.Token == token)
            {
                IsReadingSupplies = false;
            }
        }
    }

    private static string DescribeToner(TonerStatus toner)
    {
        var parts = new List<string>();
        if (toner.BlackPercent.HasValue)
        {
            parts.Add($"Black {toner.BlackPercent}%");
        }

        if (toner.CyanPercent.HasValue)
        {
            parts.Add($"Cyan {toner.CyanPercent}%");
        }

        if (toner.MagentaPercent.HasValue)
        {
            parts.Add($"Magenta {toner.MagentaPercent}%");
        }

        if (toner.YellowPercent.HasValue)
        {
            parts.Add($"Yellow {toner.YellowPercent}%");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "Supplies reported (levels not available).";
    }

    [RelayCommand]
    private async Task PrintTestPageAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await printerActionsService.PrintTestPageAsync(SelectedPrinter.Name, CancellationToken.None);
            StatusText = result.Message;
            await LoadJobsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        if (!Confirm($"Clear all queued jobs for \"{SelectedPrinter.Name}\"?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await printerActionsService.ClearQueueAsync(SelectedPrinter.Name, CancellationToken.None);
            StatusText = result.Message;
            await LoadJobsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelJobAsync(PrintJob? job)
    {
        if (SelectedPrinter is null || job is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await printerActionsService.CancelJobAsync(SelectedPrinter.Name, job.Id, CancellationToken.None);
            StatusText = result.Message;
            await LoadJobsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemovePrinterAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        var name = SelectedPrinter.Name;
        if (!Confirm($"Remove the printer \"{name}\" from this computer? This affects the current Windows account."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await printerActionsService.RemovePrinterAsync(name, CancellationToken.None);
            StatusText = result.Message;
            if (result.Success)
            {
                WeakReferenceMessenger.Default.Send(new PrintersChangedMessage());
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartSpoolerAsync()
    {
        if (!Confirm("Restart the Windows Print Spooler? Any jobs currently printing may be lost."))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Restarting Print Spooler…";
        try
        {
            var result = await spoolerServiceProvider.RestartSpoolerAsync(CancellationToken.None);
            StatusText = result.Message;
            await LoadJobsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool Confirm(string message) =>
        MessageBox.Show(message, "ISG Printer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
