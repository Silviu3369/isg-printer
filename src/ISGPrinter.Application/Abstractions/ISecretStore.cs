namespace ISGPrinter.Application.Abstractions;

/// <summary>
/// Stores named secrets (SNMP community / v3 passwords) encrypted at rest.
/// Profiles reference secrets by name; the plaintext never lands in settings.json.
/// </summary>
public interface ISecretStore
{
    Task<string> GetAsync(string name, CancellationToken cancellationToken);

    Task SetAsync(string name, string value, CancellationToken cancellationToken);
}
