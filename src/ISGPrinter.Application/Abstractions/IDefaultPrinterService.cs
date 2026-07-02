using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IDefaultPrinterService
{
    Task<OperationResult> SetDefaultPrinterAsync(string printerNameOrUncPath, CancellationToken cancellationToken);
}
