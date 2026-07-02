using System.Text.Json;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Settings;

/// <summary>
/// Portable mode: settings live only in memory for the current session and are
/// never written to disk, so the app always starts fresh with no footprint.
/// Each Load returns an independent copy (matching the file-based semantics).
/// </summary>
public sealed class InMemorySettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private AppSettings settings = new();

    public string SettingsPath => "In-memory (portable mode — not saved to disk)";

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(Clone(settings));
        }
    }

    public Task<OperationResult> SaveAsync(AppSettings appSettings, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            settings = Clone(appSettings);
        }

        return Task.FromResult(OperationResult.Ok("Settings applied for this session (portable mode — not saved to disk)."));
    }

    private static AppSettings Clone(AppSettings source) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, SerializerOptions), SerializerOptions)
        ?? new AppSettings();
}
