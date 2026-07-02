using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.App.ViewModels;

public sealed partial class DiagnosticsViewModel(
    ILocalPrinterService localPrinterService,
    IPrinterDiagnosticsService diagnosticsService,
    ISpoolerServiceProvider spoolerServiceProvider,
    IPrinterActionsService printerActionsService,
    IDefaultPrinterService defaultPrinterService) : ObservableObject
{
    private CancellationTokenSource? cts;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<LocalPrinter> Printers { get; } = [];

    public ObservableCollection<PrinterDiagnosticCheck> Checks { get; } = [];

    public ObservableCollection<string> RecommendedSteps { get; } = [];

    [ObservableProperty]
    private LocalPrinter? selectedPrinter;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasResult;

    [ObservableProperty]
    private DiagnosticStatus overallStatus = DiagnosticStatus.Unknown;

    [ObservableProperty]
    private string overallText = string.Empty;

    public PrinterDiagnosticResult? LastResult { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            var printers = await localPrinterService.GetLocalPrintersAsync(CancellationToken.None);
            Printers.Clear();
            foreach (var printer in printers)
            {
                Printers.Add(printer);
            }

            SelectedPrinter ??= Printers.FirstOrDefault(printer => printer.IsDefault) ?? Printers.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not list printers: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        var target = SelectedPrinter;
        if (target is null)
        {
            StatusText = "Select a printer to diagnose.";
            return;
        }

        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        IsLoading = true;
        HasResult = false;
        StatusText = $"Running diagnostics for {target.Name}…";
        Checks.Clear();
        RecommendedSteps.Clear();

        try
        {
            var device = new PrinterDevice
            {
                Name = target.Name,
                UncPath = target.UncPath,
                ServerName = target.ServerName,
                DriverName = target.DriverName,
                PortName = target.PortName,
                IsDefault = target.IsDefault,
                IsInstalledLocally = true
            };

            var result = await diagnosticsService.DiagnoseAsync(device, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            LastResult = result;
            foreach (var check in result.Checks)
            {
                Checks.Add(check);
            }

            foreach (var step in result.RecommendedSteps)
            {
                RecommendedSteps.Add(step);
            }

            OverallStatus = result.OverallStatus;
            OverallText = BuildOverallText(result.OverallStatus);
            HasResult = true;
            StatusText = $"Diagnostics complete for {target.Name}.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer run.
        }
        catch (Exception ex)
        {
            StatusText = $"Diagnostics failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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
        }
        finally
        {
            IsBusy = false;
        }

        await RunDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (SelectedPrinter is null)
        {
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
        }
        finally
        {
            IsBusy = false;
        }

        await RunDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task SetDefaultAsync()
    {
        if (SelectedPrinter is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await defaultPrinterService.SetDefaultPrinterAsync(SelectedPrinter.Name, CancellationToken.None);
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

        await RunDiagnosticsAsync();
    }

    private static bool Confirm(string message) =>
        MessageBox.Show(message, "ISG Printer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static string BuildOverallText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "All checks passed — no problems found.",
        DiagnosticStatus.Warning => "Completed with warnings — review the flagged checks.",
        DiagnosticStatus.Error => "Problems found — see the failed checks and recommended steps.",
        _ => "Diagnostics complete."
    };
}
