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

public sealed partial class PrintersViewModel(
    IPrinterDiscoveryService printerDiscoveryService,
    IPrinterInstallService printerInstallService,
    IDefaultPrinterService defaultPrinterService,
    IPrintServerAutodetectService autodetectService,
    INetworkPrinterScanner networkPrinterScanner,
    ISnmpQueryService snmpQueryService,
    ISettingsService settingsService) : ObservableObject
{
    // Direct-IP printers found by the network scan; kept so a server refresh
    // doesn't wipe them.
    private readonly List<PrinterDevice> scannedPrinters = [];
    private CancellationTokenSource? operationCts;

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

    private CancellationTokenSource? suppliesCts;

    partial void OnSelectedPrinterChanged(PrinterDevice? value)
    {
        HasLiveData = false;
        LiveToner = string.Empty;
        LiveModel = string.Empty;
        LiveSerial = string.Empty;
        LivePageCount = string.Empty;

        suppliesCts?.Cancel();

        // Auto-read supplies when the selected printer has a device target. If
        // the session has no SNMP credentials, the read reports that clearly.
        if (value is not null && !string.IsNullOrWhiteSpace(value.IpAddress))
        {
            suppliesCts = new CancellationTokenSource();
            _ = ReadSuppliesCoreAsync(value, value.IpAddress, suppliesCts.Token);
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

        var ip = SelectedPrinter.IpAddress;
        if (string.IsNullOrWhiteSpace(ip))
        {
            StatusText = "This printer has no known IP address to query over SNMP.";
            return;
        }

        suppliesCts?.Cancel();
        suppliesCts = new CancellationTokenSource();
        await ReadSuppliesCoreAsync(SelectedPrinter, ip, suppliesCts.Token);
    }

    private async Task ReadSuppliesCoreAsync(PrinterDevice printer, string ip, CancellationToken token)
    {
        IsReadingSupplies = true;
        StatusText = $"Reading {printer.Name} over SNMP…";

        try
        {
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
            // Superseded by a newer read.
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
    private ICollectionView? printersView;

    public ObservableCollection<PrinterDevice> Printers { get; } = [];

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
        if (string.IsNullOrWhiteSpace(FilterText) || item is not PrinterDevice printer)
        {
            return true;
        }

        return Matches(printer.Name)
            || Matches(printer.UncPath)
            || Matches(printer.ServerName)
            || Matches(printer.Location)
            || Matches(printer.DriverName);

        bool Matches(string value) => value.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private PrinterDevice? selectedPrinter;

    [ObservableProperty]
    private string newServerName = string.Empty;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool isLoading;

    partial void OnIsLoadingChanged(bool value) => CancelOperationCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        var operation = StartLongOperation();

        try
        {
            await LoadPrintersCoreAsync(operation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Printer discovery cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Printer discovery failed: {ex.Message}";
        }
        finally
        {
            FinishLongOperation(operation);
        }
    }

    private async Task LoadPrintersCoreAsync(CancellationToken cancellationToken)
    {
        StatusText = "Discovering printers...";

        Printers.Clear();
        var result = await printerDiscoveryService.DiscoverCompanyPrintersAsync(cancellationToken);
        foreach (var printer in result.Printers)
        {
            Printers.Add(printer);
        }

        ApplyScanned();
        StatusText = BuildStatusText(result);
    }

    private static string BuildStatusText(PrinterDiscoveryResult result)
    {
        if (result.ServersQueried == 0)
        {
            return "No print server configured yet. Add one above to discover printers.";
        }

        var summary = $"{result.Printers.Count} printer(s) from {result.ServersQueried} server(s).";

        if (result.ServerErrors.Count > 0)
        {
            var detail = string.Join("; ", result.ServerErrors.Select(error => $"{error.ServerName} — {error.Message}"));
            summary += $"  {result.ServerErrors.Count} server(s) had problems: {detail}";
        }

        return summary;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        operationCts?.Cancel();
        StatusText = "Cancelling...";
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancelOperation() =>
        IsLoading && operationCts is { IsCancellationRequested: false };

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var serverName = NormalizeServerName(NewServerName);
        if (string.IsNullOrWhiteSpace(serverName))
        {
            StatusText = "Enter a print server name.";
            return;
        }

        var operation = StartLongOperation();
        try
        {
            StatusText = $"Validating {serverName}...";
            var validation = await printerDiscoveryService.DiscoverServerPrintersAsync(serverName, operation.Token);
            if (validation.ServerErrors.Count > 0)
            {
                var error = validation.ServerErrors[0];
                StatusText = $"Could not add {serverName}: {error.Message}";
                return;
            }

            if (validation.Printers.Count == 0)
            {
                StatusText = $"Could not add {serverName}: the server responded, but no shared printers were found.";
                return;
            }

            var settings = await settingsService.LoadAsync(operation.Token);
            if (!settings.KnownPrintServers.Contains(serverName, StringComparer.OrdinalIgnoreCase))
            {
                settings.KnownPrintServers.Add(serverName);
                await settingsService.SaveAsync(settings, operation.Token);
            }

            NewServerName = string.Empty;
            await LoadPrintersCoreAsync(operation.Token);
            WeakReferenceMessenger.Default.Send(new SessionSettingsChangedMessage());
            StatusText = $"Added {serverName} for this session. Found {validation.Printers.Count} shared printer(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Add server cancelled for {serverName}.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Could not save the server list. Run ISG Printer as administrator and try again.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not add server: {ex.Message}";
        }
        finally
        {
            FinishLongOperation(operation);
        }
    }

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        var operation = StartLongOperation();
        StatusText = "Auto-detecting print servers...";

        try
        {
            var found = await autodetectService.DetectServersAsync(operation.Token);
            if (found.Count == 0)
            {
                StatusText = "Auto-detect found no print servers. Add one manually, or run on a domain PC with installed network printers.";
                return;
            }

            var settings = await settingsService.LoadAsync(operation.Token);
            var added = found
                .Where(server => !settings.KnownPrintServers.Contains(server, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (added.Count == 0)
            {
                StatusText = $"Auto-detect found {found.Count} server(s); all are already in the session list.";
                return;
            }

            settings.KnownPrintServers.AddRange(added);
            await settingsService.SaveAsync(settings, operation.Token);
            await LoadPrintersCoreAsync(operation.Token);
            WeakReferenceMessenger.Default.Send(new SessionSettingsChangedMessage());
            StatusText = $"Auto-detect added {added.Count} server(s) for this session: {string.Join(", ", added)}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Auto-detect cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Found servers, but could not save the list. Run ISG Printer as administrator and try again.";
        }
        catch (Exception ex)
        {
            StatusText = $"Auto-detect failed: {ex.Message}";
        }
        finally
        {
            FinishLongOperation(operation);
        }
    }

    [RelayCommand]
    private async Task ScanNetworkAsync()
    {
        var operation = StartLongOperation();
        StatusText = "Scanning the local network for printers (port 9100)...";

        try
        {
            var found = await networkPrinterScanner.ScanAsync(operation.Token);

            scannedPrinters.Clear();
            foreach (var device in found)
            {
                scannedPrinters.Add(new PrinterDevice
                {
                    Id = device.IpAddress,
                    Name = string.IsNullOrWhiteSpace(device.Name) ? $"Network printer @ {device.IpAddress}" : device.Name,
                    IpAddress = device.IpAddress,
                    ServerName = "Network (direct IP)",
                    DriverName = device.Model
                });
            }

            ApplyScanned();
            StatusText = found.Count == 0
                ? "No network printers found on this subnet (nothing answered on port 9100)."
                : $"Found {found.Count} network printer(s) on this subnet.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Network scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Network scan failed: {ex.Message}";
        }
        finally
        {
            FinishLongOperation(operation);
        }
    }

    private void ApplyScanned()
    {
        foreach (var device in scannedPrinters)
        {
            var alreadyListed = !string.IsNullOrWhiteSpace(device.IpAddress)
                && Printers.Any(printer => string.Equals(printer.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase));

            if (!alreadyListed)
            {
                Printers.Add(device);
            }
        }
    }

    private CancellationTokenSource StartLongOperation()
    {
        operationCts?.Cancel();
        operationCts = new CancellationTokenSource();
        IsLoading = true;
        CancelOperationCommand.NotifyCanExecuteChanged();
        return operationCts;
    }

    private void FinishLongOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(operationCts, operation))
        {
            operationCts = null;
            IsLoading = false;
            CancelOperationCommand.NotifyCanExecuteChanged();
        }

        operation.Dispose();
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        var result = await printerInstallService.InstallPrinterAsync(SelectedPrinter, CancellationToken.None);
        StatusText = result.Message;
        if (result.Success)
        {
            WeakReferenceMessenger.Default.Send(new PrintersChangedMessage());
        }
    }

    [RelayCommand]
    private async Task SetDefaultSelectedAsync()
    {
        if (SelectedPrinter is null)
        {
            StatusText = "Select a printer first.";
            return;
        }

        var target = string.IsNullOrWhiteSpace(SelectedPrinter.UncPath) ? SelectedPrinter.Name : SelectedPrinter.UncPath;
        var result = await defaultPrinterService.SetDefaultPrinterAsync(target, CancellationToken.None);
        StatusText = result.Message;
        if (result.Success)
        {
            WeakReferenceMessenger.Default.Send(new PrintersChangedMessage());
        }
    }

    [RelayCommand]
    private void CopyUnc()
    {
        if (SelectedPrinter is null || string.IsNullOrWhiteSpace(SelectedPrinter.UncPath))
        {
            StatusText = "Selected printer has no UNC path.";
            return;
        }

        Clipboard.SetText(SelectedPrinter.UncPath);
        StatusText = "UNC path copied.";
    }

    private static string NormalizeServerName(string value) =>
        value.Trim().TrimStart('\\');
}
