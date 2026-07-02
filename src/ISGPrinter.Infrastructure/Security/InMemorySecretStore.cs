using System.Collections.Concurrent;
using ISGPrinter.Application.Abstractions;

namespace ISGPrinter.Infrastructure.Security;

/// <summary>
/// Portable mode: SNMP secrets are kept only in memory for the current session
/// and never written to disk. They reset when the app closes.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> secrets = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> GetAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(secrets.TryGetValue(name, out var value) ? value : string.Empty);

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(value))
        {
            secrets.TryRemove(name, out _);
        }
        else
        {
            secrets[name] = value;
        }

        return Task.CompletedTask;
    }
}
