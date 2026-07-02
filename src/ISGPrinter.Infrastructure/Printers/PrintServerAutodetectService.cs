using System.DirectoryServices.Protocols;
using ISGPrinter.Application.Abstractions;

namespace ISGPrinter.Infrastructure.Printers;

public sealed class PrintServerAutodetectService(
    ILocalPrinterService localPrinterService,
    IAppEnvironmentService environmentService,
    ISettingsService settingsService) : IPrintServerAutodetectService
{
    public async Task<IReadOnlyList<string>> DetectServersAsync(CancellationToken cancellationToken)
    {
        var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = await settingsService.LoadAsync(cancellationToken);

        // Source 1: servers behind printers already installed on this PC.
        try
        {
            var printers = await localPrinterService.GetLocalPrintersAsync(cancellationToken);
            foreach (var printer in printers)
            {
                var server = ExtractServer(printer.UncPath) ?? Normalize(printer.ServerName);
                if (!string.IsNullOrWhiteSpace(server))
                {
                    servers.Add(server);
                }
            }
        }
        catch
        {
            // Best effort.
        }

        // Source 2: print queues published in Active Directory (domain only).
        var environment = await environmentService.GetEnvironmentAsync(cancellationToken);
        if (settings.EnableActiveDirectoryDiscovery
            && environment.IsDomainJoined
            && !string.IsNullOrWhiteSpace(environment.DomainName))
        {
            var adServers = await Task.Run(() => DetectFromActiveDirectory(environment.DomainName, cancellationToken), cancellationToken);
            foreach (var server in adServers)
            {
                var normalized = Normalize(server);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    servers.Add(normalized);
                }
            }
        }

        return servers.OrderBy(server => server, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> DetectFromActiveDirectory(string domainFqdn, CancellationToken cancellationToken)
    {
        var servers = new List<string>();

        try
        {
            var identifier = new LdapDirectoryIdentifier(domainFqdn);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
                Timeout = TimeSpan.FromSeconds(15)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.Bind();

            var baseDn = ReadDefaultNamingContext(connection);
            if (string.IsNullOrWhiteSpace(baseDn))
            {
                return servers;
            }

            var request = new SearchRequest(baseDn, "(objectCategory=printQueue)", SearchScope.Subtree, "uNCName", "shortServerName");
            var pageControl = new PageResultRequestControl(500);
            request.Controls.Add(pageControl);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = (SearchResponse)connection.SendRequest(request);
                foreach (SearchResultEntry entry in response.Entries)
                {
                    var server = ExtractServerFromEntry(entry);
                    if (!string.IsNullOrWhiteSpace(server))
                    {
                        servers.Add(server);
                    }
                }

                var pageResponse = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (pageResponse is null || pageResponse.Cookie.Length == 0)
                {
                    break;
                }

                pageControl.Cookie = pageResponse.Cookie;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Active Directory may be unreachable or access-denied; treat as no results.
        }

        return servers;
    }

    private static string? ReadDefaultNamingContext(LdapConnection connection)
    {
        var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0)
        {
            return null;
        }

        var attribute = response.Entries[0].Attributes["defaultNamingContext"];
        return attribute is { Count: > 0 } ? attribute[0]?.ToString() : null;
    }

    private static string? ExtractServerFromEntry(SearchResultEntry entry)
    {
        var shortName = FirstValue(entry, "shortServerName");
        if (!string.IsNullOrWhiteSpace(shortName))
        {
            return shortName;
        }

        return ExtractServer(FirstValue(entry, "uNCName"));
    }

    private static string? FirstValue(SearchResultEntry entry, string attributeName)
    {
        var attribute = entry.Attributes[attributeName];
        return attribute is { Count: > 0 } ? attribute[0]?.ToString() : null;
    }

    private static string? ExtractServer(string? uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = uncPath[2..];
        var slash = rest.IndexOf('\\');
        var server = slash >= 0 ? rest[..slash] : rest;
        return string.IsNullOrWhiteSpace(server) ? null : server;
    }

    private static string Normalize(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return string.Empty;
        }

        var trimmed = serverName.Trim().TrimStart('\\');
        var slash = trimmed.IndexOf('\\');
        if (slash >= 0)
        {
            trimmed = trimmed[..slash];
        }

        return trimmed;
    }
}
