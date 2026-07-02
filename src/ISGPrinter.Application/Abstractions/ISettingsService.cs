using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface ISettingsService
{
    string SettingsPath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
