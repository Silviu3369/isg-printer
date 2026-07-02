using System.Text.Json;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class PrinterDiscoveryService(
    ISettingsService settingsService,
    ILocalPrinterService localPrinterService) : IPrinterDiscoveryService
{
    private static readonly TimeSpan PerServerTimeout = TimeSpan.FromSeconds(25);

    // Lists shared printers on a print server and joins each printer's port to
    // its host address (IP/DNS), so the IP column is real, not a placeholder.
    // The target server name arrives via $env:ISG_TARGET — never interpolated.
    private const string DiscoveryScript = """
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $target = $env:ISG_TARGET
        try {
            $printers = @(Get-Printer -ComputerName $target | Where-Object { $_.Shared })
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 2
        }
        $ports = @{}
        try {
            Get-PrinterPort -ComputerName $target | ForEach-Object {
                if ($_.Name) { $ports[[string]$_.Name] = $_.PrinterHostAddress }
            }
        } catch { }
        $printers | ForEach-Object {
            [pscustomobject]@{
                Name         = $_.Name
                ShareName    = $_.ShareName
                ComputerName = $_.ComputerName
                Location     = $_.Location
                DriverName   = $_.DriverName
                PortName     = $_.PortName
                IpAddress    = $ports[[string]$_.PortName]
            }
        } | ConvertTo-Json -Depth 3
        """;

    public async Task<PrinterDiscoveryResult> DiscoverCompanyPrintersAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var localPrinters = await localPrinterService.GetLocalPrintersAsync(cancellationToken);

        var servers = settings.KnownPrintServers
            .Select(NormalizeServerName)
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Query servers in parallel (capped) so several print servers don't
        // stack up sequentially.
        using var gate = new SemaphoreSlim(4);
        var perServer = await Task.WhenAll(
            servers.Select(server => QueryServerAsync(server, localPrinters, gate, cancellationToken)));

        var printers = perServer.Where(item => item.Printers is not null).SelectMany(item => item.Printers!).ToList();
        var errors = perServer.Where(item => item.Error is not null).Select(item => item.Error!).ToList();

        return new PrinterDiscoveryResult
        {
            Printers = DeduplicatePrinters(printers),
            ServerErrors = errors,
            ServersQueried = servers.Count
        };
    }

    public async Task<PrinterDiscoveryResult> DiscoverServerPrintersAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var server = NormalizeServerName(serverName);
        if (string.IsNullOrWhiteSpace(server))
        {
            return new PrinterDiscoveryResult();
        }

        var localPrinters = await localPrinterService.GetLocalPrintersAsync(cancellationToken);
        using var gate = new SemaphoreSlim(1);
        var result = await QueryServerAsync(server, localPrinters, gate, cancellationToken);

        return new PrinterDiscoveryResult
        {
            Printers = result.Printers is null ? [] : DeduplicatePrinters(result.Printers),
            ServerErrors = result.Error is null ? [] : [result.Error],
            ServersQueried = 1
        };
    }

    private static List<PrinterDevice> DeduplicatePrinters(IEnumerable<PrinterDevice> printers) =>
        printers
            .Where(printer => !string.IsNullOrWhiteSpace(printer.UncPath))
            .GroupBy(printer => printer.UncPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(printer => printer.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<(IReadOnlyList<PrinterDevice>? Printers, PrinterServerError? Error)> QueryServerAsync(
        string server,
        IReadOnlyList<LocalPrinter> localPrinters,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var run = await PowerShellRunner.RunAsync(
                DiscoveryScript,
                new Dictionary<string, string> { ["ISG_TARGET"] = server },
                PerServerTimeout,
                cancellationToken);

            if (run.TimedOut)
            {
                return (null, new PrinterServerError
                {
                    ServerName = server,
                    Message = "Timed out — server unreachable or blocked by a firewall."
                });
            }

            if (!run.Succeeded)
            {
                return (null, new PrinterServerError { ServerName = server, Message = DescribeError(run.StandardError) });
            }

            return (ParsePrinterJson(run.StandardOutput, server, localPrinters), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, new PrinterServerError { ServerName = server, Message = ex.Message });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<PrintServer>> DiscoverPrintServersAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return settings.KnownPrintServers
            .Select(NormalizeServerName)
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(server => new PrintServer
            {
                Name = server,
                LastChecked = DateTimeOffset.Now
            })
            .ToList();
    }

    private static string DescribeError(string standardError)
    {
        var message = standardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(message))
        {
            return "The print server returned an error.";
        }

        if (Contains(message, "access is denied") || Contains(message, "denied"))
        {
            return "Access denied — you need print or admin rights on this server.";
        }

        if (Contains(message, "rpc server is unavailable")
            || Contains(message, "cannot find")
            || Contains(message, "could not be resolved")
            || Contains(message, "no such host")
            || Contains(message, "reachable")
            || Contains(message, "spooler"))
        {
            return "Server unreachable — check the name, that it is online, and that its Print Spooler is running.";
        }

        return message;

        static bool Contains(string value, string token) =>
            value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeServerName(string serverName) =>
        serverName.Trim().TrimStart('\\');

    private static IReadOnlyList<PrinterDevice> ParsePrinterJson(
        string json,
        string fallbackServerName,
        IReadOnlyList<LocalPrinter> localPrinters)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var elements = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : [root];

        return elements
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element => MapPrinter(element, fallbackServerName, localPrinters))
            .Where(printer => !string.IsNullOrWhiteSpace(printer.Name))
            .ToList();
    }

    private static PrinterDevice MapPrinter(
        JsonElement element,
        string fallbackServerName,
        IReadOnlyList<LocalPrinter> localPrinters)
    {
        var name = ReadString(element, "Name");
        var shareName = ReadString(element, "ShareName");
        var serverName = ReadString(element, "ComputerName");

        if (string.IsNullOrWhiteSpace(serverName))
        {
            serverName = fallbackServerName;
        }

        var uncPath = !string.IsNullOrWhiteSpace(shareName) ? $@"\\{serverName}\{shareName}" : string.Empty;
        var localMatch = localPrinters.FirstOrDefault(local =>
            (!string.IsNullOrWhiteSpace(uncPath) && string.Equals(local.UncPath, uncPath, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(local.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(local.Name, uncPath, StringComparison.OrdinalIgnoreCase));

        return new PrinterDevice
        {
            Id = string.IsNullOrWhiteSpace(uncPath) ? $"{serverName}:{name}" : uncPath,
            Name = name,
            ShareName = shareName,
            ServerName = serverName,
            UncPath = uncPath,
            Location = ReadString(element, "Location"),
            DriverName = ReadString(element, "DriverName"),
            PortName = ReadString(element, "PortName"),
            IpAddress = ReadString(element, "IpAddress"),
            IsInstalledLocally = localMatch is not null,
            IsDefault = localMatch?.IsDefault ?? false
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return property.ToString();
    }
}
