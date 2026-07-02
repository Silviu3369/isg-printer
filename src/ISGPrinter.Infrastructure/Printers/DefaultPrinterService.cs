using System.Management;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class DefaultPrinterService(ILocalPrinterService localPrinterService) : IDefaultPrinterService
{
    public async Task<OperationResult> SetDefaultPrinterAsync(string printerNameOrUncPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerNameOrUncPath))
        {
            return OperationResult.Fail(OperationStatus.InvalidInput, "Printer name is required.");
        }

        var printers = await localPrinterService.GetLocalPrintersAsync(cancellationToken);
        var printer = printers.FirstOrDefault(item =>
            string.Equals(item.Name, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.UncPath, printerNameOrUncPath, StringComparison.OrdinalIgnoreCase));

        if (printer is null)
        {
            return OperationResult.Fail(OperationStatus.NotFound, "Printer is not installed for the current Windows account.");
        }

        try
        {
            var escapedName = printer.Name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name = '{escapedName}'");
            using var managementPrinter = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

            if (managementPrinter is null)
            {
                return OperationResult.Fail(OperationStatus.NotFound, "Printer could not be found through WMI.");
            }

            var result = managementPrinter.InvokeMethod("SetDefaultPrinter", null);
            var returnValue = Convert.ToUInt32(result);

            return returnValue == 0
                ? OperationResult.Ok("Default printer updated for the current Windows account.")
                : OperationResult.Fail(OperationStatus.Failed, "Windows did not accept the default printer change.", $"WMI return value: {returnValue}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationStatus.Failed, "Could not set the default printer.", ex.Message);
        }
    }
}
