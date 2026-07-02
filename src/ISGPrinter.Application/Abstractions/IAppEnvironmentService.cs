using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface IAppEnvironmentService
{
    Task<AppEnvironmentInfo> GetEnvironmentAsync(CancellationToken cancellationToken);
}
