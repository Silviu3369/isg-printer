namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// Discovers candidate print server names without the technician typing them:
/// from printers already installed on this PC, and (when domain-joined) from
/// print queues published in Active Directory.
/// </summary>
public interface IPrintServerAutodetectService
{
    Task<IReadOnlyList<string>> DetectServersAsync(CancellationToken cancellationToken);
}
