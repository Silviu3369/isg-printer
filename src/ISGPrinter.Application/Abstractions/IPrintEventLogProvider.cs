using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// Reads recent error/warning entries from the Windows
/// <c>Microsoft-Windows-PrintService</c> event channel.
/// </summary>
public interface IPrintEventLogProvider
{
    Task<IReadOnlyList<PrintEventEntry>> GetRecentPrintErrorsAsync(TimeSpan window, int maxEntries, CancellationToken cancellationToken);
}
