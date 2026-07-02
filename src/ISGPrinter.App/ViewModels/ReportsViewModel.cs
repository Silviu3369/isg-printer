using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;
using Microsoft.Win32;

namespace ISGPrinter.App.ViewModels;

public sealed partial class ReportsViewModel(
    IAppEnvironmentService environmentService,
    ILocalPrinterService localPrinterService,
    IPrinterDiagnosticsService diagnosticsService,
    IPrinterReportService reportService,
    ISettingsService settingsService) : ObservableObject
{
    private CancellationTokenSource? cts;

    public ObservableCollection<LocalPrinter> Printers { get; } = [];

    [ObservableProperty]
    private LocalPrinter? selectedPrinter;

    [ObservableProperty]
    private string reportContent = string.Empty;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasReport;

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
    private async Task GenerateReportAsync()
    {
        var target = SelectedPrinter;
        if (target is null)
        {
            StatusText = "Select a printer to report on.";
            return;
        }

        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        IsLoading = true;
        StatusText = $"Generating report for {target.Name}…";

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

            var diagnosticResult = await diagnosticsService.DiagnoseAsync(device, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var environment = await environmentService.GetEnvironmentAsync(token);
            ReportContent = reportService.GenerateTicketReport(diagnosticResult, environment);
            HasReport = true;
            StatusText = $"Report generated for {target.Name}.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer run.
        }
        catch (Exception ex)
        {
            StatusText = $"Report generation failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(ReportContent))
        {
            StatusText = "Generate a report first.";
            return;
        }

        Clipboard.SetText(ReportContent);
        StatusText = "Report copied to clipboard.";
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportContent))
        {
            StatusText = "Generate a report first.";
            return;
        }

        var settings = await settingsService.LoadAsync(CancellationToken.None);
        var initialDirectory = Environment.ExpandEnvironmentVariables(settings.DefaultExportFolder ?? string.Empty);

        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostic report",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"ISG-Printer-Report-{SanitizeFileName(SelectedPrinter?.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await reportService.ExportReportAsync(ReportContent, dialog.FileName, CancellationToken.None);
        StatusText = result.Success ? $"Report saved to {dialog.FileName}" : result.Message;
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "printer";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim('_', ' ', '\\');
    }
}
