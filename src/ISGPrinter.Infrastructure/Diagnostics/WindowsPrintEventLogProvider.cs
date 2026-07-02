using System.Text.Json;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;
using ISGPrinter.Infrastructure.Printers;

namespace ISGPrinter.Infrastructure.Diagnostics;

/// <summary>
/// Reads recent Print Spooler errors/warnings from the
/// <c>Microsoft-Windows-PrintService/Admin</c> channel via Get-WinEvent,
/// reusing the hardened PowerShell runner (concurrent read, timeout, kill).
/// </summary>
public sealed class WindowsPrintEventLogProvider : IPrintEventLogProvider
{
    private const string Script = """
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $hours = [int]$env:ISG_HOURS
        $max = [int]$env:ISG_MAX
        $since = (Get-Date).AddHours(-$hours)
        try {
            $events = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-PrintService/Admin'; Level = 2, 3; StartTime = $since } -MaxEvents $max -ErrorAction Stop
        } catch {
            if ($_.Exception.Message -match 'No events were found') { '[]'; exit 0 }
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 2
        }
        $events | ForEach-Object {
            [pscustomobject]@{
                Time    = $_.TimeCreated.ToString('o')
                Id      = $_.Id
                Level   = $_.LevelDisplayName
                Message = (($_.Message -split "`r?`n") | Where-Object { $_ } | Select-Object -First 1)
            }
        } | ConvertTo-Json -Depth 3
        """;

    public async Task<IReadOnlyList<PrintEventEntry>> GetRecentPrintErrorsAsync(TimeSpan window, int maxEntries, CancellationToken cancellationToken)
    {
        var run = await PowerShellRunner.RunAsync(
            Script,
            new Dictionary<string, string>
            {
                ["ISG_HOURS"] = ((int)Math.Max(1, window.TotalHours)).ToString(),
                ["ISG_MAX"] = Math.Max(1, maxEntries).ToString()
            },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (!run.Succeeded || string.IsNullOrWhiteSpace(run.StandardOutput))
        {
            return [];
        }

        return Parse(run.StandardOutput);
    }

    private static IReadOnlyList<PrintEventEntry> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : [root];

            var entries = new List<PrintEventEntry>();
            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries.Add(new PrintEventEntry
                {
                    Time = ReadDate(element, "Time"),
                    Id = ReadInt(element, "Id"),
                    Level = ReadString(element, "Level"),
                    Message = ReadString(element, "Message")
                });
            }

            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.ToString()
            : string.Empty;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static DateTimeOffset ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && DateTimeOffset.TryParse(value.ToString(), out var date)
            ? date
            : DateTimeOffset.MinValue;
}
