using System.Text.Json;
using ISGPrinter.Application.Abstractions;

namespace ISGPrinter.Infrastructure.Security;

/// <summary>
/// Persists secrets as a DPAPI-protected name → value map next to settings.json.
/// </summary>
public sealed class FileSecretStore(ICredentialProtector protector) : ISecretStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly string filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ISG",
        "ISG Printer",
        "secrets.dat");

    public async Task<string> GetAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var map = await LoadAsync(cancellationToken);
        if (map.TryGetValue(name, out var protectedValue) && !string.IsNullOrEmpty(protectedValue))
        {
            try
            {
                return protector.Unprotect(protectedValue);
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var map = await LoadAsync(cancellationToken);

            if (string.IsNullOrEmpty(value))
            {
                map.Remove(name);
            }
            else
            {
                map[name] = protector.Protect(value);
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, map, SerializerOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = File.OpenRead(filePath);
            var map = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, SerializerOptions, cancellationToken);
            return map is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
