using System.Net;
using System.Net.Sockets;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace ISGPrinter.Infrastructure.Snmp;

/// <summary>
/// Reads printer state over SNMP (Printer-MIB / Host Resources MIB), supporting
/// v2c and v3. Secrets (community / v3 passwords) are resolved from the secret
/// store using the names carried by the profile. Uses GET-only probing (no
/// walk) over a fixed set of supply indices for a single, robust code path.
/// </summary>
public sealed class SnmpPrinterProvider(ISecretStore secretStore) : ISnmpPrinterProvider
{
    private const int SnmpPort = 161;
    private const int MaxSupplyIndex = 12;

    // Printer-MIB / Host Resources MIB scalar OIDs.
    private const string OidSysDescr = "1.3.6.1.2.1.1.1.0";
    private const string OidPrinterName = "1.3.6.1.2.1.43.5.1.1.16.1.1";
    private const string OidSerialNumber = "1.3.6.1.2.1.43.5.1.1.17.1.1";
    private const string OidPageCount = "1.3.6.1.2.1.43.10.1.1.5.1.1";
    private const string OidPrinterStatus = "1.3.6.1.2.1.25.3.5.1.1.1";

    // Supply table column prefixes (index appended).
    private const string OidSupplyDescription = "1.3.6.1.2.1.43.11.1.1.6.1.";
    private const string OidSupplyMaxCapacity = "1.3.6.1.2.1.43.11.1.1.8.1.";
    private const string OidSupplyLevel = "1.3.6.1.2.1.43.11.1.1.9.1.";

    private sealed record SnmpCredentials(string Community, string AuthPassword, string PrivacyPassword);

    public async Task<SnmpResult<TonerStatus>> GetTonerStatusAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await ResolveCredentialsAsync(profile, cancellationToken);
            if (credentials.ResultMessage.Length > 0)
            {
                return SnmpResult<TonerStatus>.Unavailable(credentials.ResultMessage);
            }

            var oids = new List<string>();
            for (var index = 1; index <= MaxSupplyIndex; index++)
            {
                oids.Add(OidSupplyDescription + index);
                oids.Add(OidSupplyMaxCapacity + index);
                oids.Add(OidSupplyLevel + index);
            }

            var values = await QueryAsync(ipAddress, profile, oids, credentials.Value!, timeout, cancellationToken);
            var toner = BuildTonerStatus(values);
            return toner.IsAvailable
                ? new SnmpResult<TonerStatus> { Success = true, Value = toner, Message = "Toner read over SNMP." }
                : SnmpResult<TonerStatus>.Unavailable("The printer did not report supply levels over SNMP.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<TonerStatus>(ex);
        }
    }

    public async Task<SnmpResult<PrinterHardwareInfo>> GetHardwareInfoAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await ResolveCredentialsAsync(profile, cancellationToken);
            if (credentials.ResultMessage.Length > 0)
            {
                return SnmpResult<PrinterHardwareInfo>.Unavailable(credentials.ResultMessage);
            }

            var values = await QueryAsync(ipAddress, profile, [OidPrinterName, OidSerialNumber, OidSysDescr], credentials.Value!, timeout, cancellationToken);
            var info = new PrinterHardwareInfo
            {
                Model = ReadText(values, OidPrinterName),
                SerialNumber = ReadText(values, OidSerialNumber),
                Manufacturer = ReadText(values, OidSysDescr)
            };

            var hasData = !string.IsNullOrWhiteSpace(info.Model)
                || !string.IsNullOrWhiteSpace(info.SerialNumber)
                || !string.IsNullOrWhiteSpace(info.Manufacturer);

            return hasData
                ? new SnmpResult<PrinterHardwareInfo> { Success = true, Value = info, Message = "Hardware info read over SNMP." }
                : SnmpResult<PrinterHardwareInfo>.Unavailable("The printer did not report hardware info over SNMP.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<PrinterHardwareInfo>(ex);
        }
    }

    public async Task<SnmpResult<long?>> GetPageCounterAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await ResolveCredentialsAsync(profile, cancellationToken);
            if (credentials.ResultMessage.Length > 0)
            {
                return SnmpResult<long?>.Unavailable(credentials.ResultMessage);
            }

            var values = await QueryAsync(ipAddress, profile, [OidPageCount], credentials.Value!, timeout, cancellationToken);
            var count = ReadLong(values, OidPageCount);
            return count.HasValue
                ? new SnmpResult<long?> { Success = true, Value = count, Message = "Page counter read over SNMP." }
                : SnmpResult<long?>.Unavailable("The printer did not report a page counter over SNMP.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<long?>(ex);
        }
    }

    public async Task<SnmpResult<string>> GetRawPrinterStatusAsync(
        string ipAddress,
        SnmpProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await ResolveCredentialsAsync(profile, cancellationToken);
            if (credentials.ResultMessage.Length > 0)
            {
                return SnmpResult<string>.Unavailable(credentials.ResultMessage);
            }

            var values = await QueryAsync(ipAddress, profile, [OidPrinterStatus], credentials.Value!, timeout, cancellationToken);
            var status = DescribePrinterStatus(ReadLong(values, OidPrinterStatus));
            return new SnmpResult<string> { Success = true, Value = status, Message = "Status read over SNMP." };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<string>(ex);
        }
    }

    private async Task<(SnmpCredentials? Value, string ResultMessage)> ResolveCredentialsAsync(
        SnmpProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Version == SnmpVersion.V2C)
        {
            var community = await secretStore.GetAsync(profile.CommunitySecretName, cancellationToken);
            if (string.IsNullOrWhiteSpace(community))
            {
                return (null, "SNMP v2c community is not configured for this session.");
            }

            return (new SnmpCredentials(community, string.Empty, string.Empty), string.Empty);
        }

        if (string.IsNullOrWhiteSpace(profile.UserName))
        {
            return (null, "SNMP v3 user name is not configured for this session.");
        }

        var authPassword = string.Empty;
        if (profile.AuthenticationProtocol != SnmpAuthenticationProtocol.None)
        {
            authPassword = await secretStore.GetAsync(profile.AuthSecretName, cancellationToken);
            if (string.IsNullOrWhiteSpace(authPassword))
            {
                return (null, "SNMP v3 authentication password is not configured for this session.");
            }
        }

        var privacyPassword = string.Empty;
        if (profile.PrivacyProtocol != SnmpPrivacyProtocol.None)
        {
            if (profile.AuthenticationProtocol == SnmpAuthenticationProtocol.None)
            {
                return (null, "SNMP v3 privacy requires an authentication protocol.");
            }

            privacyPassword = await secretStore.GetAsync(profile.PrivacySecretName, cancellationToken);
            if (string.IsNullOrWhiteSpace(privacyPassword))
            {
                return (null, "SNMP v3 privacy password is not configured for this session.");
            }
        }

        return (new SnmpCredentials(string.Empty, authPassword, privacyPassword), string.Empty);
    }

    private async Task<IReadOnlyDictionary<string, ISnmpData>> QueryAsync(
        string ipAddress,
        SnmpProfile profile,
        IReadOnlyList<string> oids,
        SnmpCredentials credentials,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var endpoint = await ResolveEndpointAsync(ipAddress, cancellationToken);
        var variables = oids.Select(oid => new Variable(new ObjectIdentifier(oid))).ToList();
        var timeoutMs = ToTimeoutMilliseconds(timeout);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return profile.Version == SnmpVersion.V3
                ? QueryV3(endpoint, profile, variables, credentials, timeoutMs)
                : QueryV2C(endpoint, variables, credentials, timeoutMs);
        }, cancellationToken);
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds > 0 ? (int)timeout.TotalMilliseconds : 2500;
        return Math.Clamp(milliseconds, 500, 15000);
    }

    private static IReadOnlyDictionary<string, ISnmpData> QueryV2C(
        IPEndPoint endpoint,
        IList<Variable> variables,
        SnmpCredentials credentials,
        int timeoutMs)
    {
        var reply = Messenger.Get(VersionCode.V2, endpoint, new OctetString(credentials.Community), variables, timeoutMs);
        return ToDictionary(reply);
    }

    private static IReadOnlyDictionary<string, ISnmpData> QueryV3(
        IPEndPoint endpoint,
        SnmpProfile profile,
        IList<Variable> variables,
        SnmpCredentials credentials,
        int timeoutMs)
    {
        var privacy = BuildPrivacyProvider(profile, credentials.AuthPassword, credentials.PrivacyPassword);

        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = discovery.GetResponse(timeoutMs, endpoint);

        var request = new GetRequestMessage(
            VersionCode.V3,
            Messenger.NextMessageId,
            Messenger.NextRequestId,
            new OctetString(profile.UserName),
            OctetString.Empty,
            variables,
            privacy,
            Messenger.MaxMessageSize,
            report);

        var reply = request.GetResponse(timeoutMs, endpoint);
        return ToDictionary(reply.Pdu().Variables);
    }

    // MD5/SHA-1/DES are flagged obsolete by the library for being weak, but they
    // remain valid SNMPv3 options that printers in the field still use, so a
    // technician tool must keep offering them. Suppressing CS0618 here is
    // intentional and scoped to these device-compatibility choices.
#pragma warning disable CS0618
    private static IPrivacyProvider BuildPrivacyProvider(SnmpProfile profile, string authPassword, string privacyPassword)
    {
        IAuthenticationProvider auth = profile.AuthenticationProtocol switch
        {
            SnmpAuthenticationProtocol.Md5 => new MD5AuthenticationProvider(new OctetString(authPassword)),
            SnmpAuthenticationProtocol.Sha1 => new SHA1AuthenticationProvider(new OctetString(authPassword)),
            SnmpAuthenticationProtocol.Sha256 => new SHA256AuthenticationProvider(new OctetString(authPassword)),
            SnmpAuthenticationProtocol.Sha384 => new SHA384AuthenticationProvider(new OctetString(authPassword)),
            SnmpAuthenticationProtocol.Sha512 => new SHA512AuthenticationProvider(new OctetString(authPassword)),
            _ => DefaultAuthenticationProvider.Instance
        };

        if (auth is DefaultAuthenticationProvider)
        {
            return DefaultPrivacyProvider.DefaultPair;
        }

        return profile.PrivacyProtocol switch
        {
            SnmpPrivacyProtocol.Des => new DESPrivacyProvider(new OctetString(privacyPassword), auth),
            SnmpPrivacyProtocol.Aes128 => new AESPrivacyProvider(new OctetString(privacyPassword), auth),
            SnmpPrivacyProtocol.Aes192 => new AES192PrivacyProvider(new OctetString(privacyPassword), auth),
            SnmpPrivacyProtocol.Aes256 => new AES256PrivacyProvider(new OctetString(privacyPassword), auth),
            _ => new DefaultPrivacyProvider(auth)
        };
    }
#pragma warning restore CS0618

    private static async Task<IPEndPoint> ResolveEndpointAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(ipAddress, out var parsed))
        {
            return new IPEndPoint(parsed, SnmpPort);
        }

        var addresses = await Dns.GetHostAddressesAsync(ipAddress, cancellationToken);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses.FirstOrDefault()
                      ?? throw new InvalidOperationException($"Could not resolve '{ipAddress}'.");
        return new IPEndPoint(address, SnmpPort);
    }

    private static IReadOnlyDictionary<string, ISnmpData> ToDictionary(IEnumerable<Variable> variables)
    {
        var map = new Dictionary<string, ISnmpData>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            map[variable.Id.ToString()] = variable.Data;
        }

        return map;
    }

    private TonerStatus BuildTonerStatus(IReadOnlyDictionary<string, ISnmpData> values)
    {
        var toner = new TonerStatus();
        var percents = new List<int>();

        for (var index = 1; index <= MaxSupplyIndex; index++)
        {
            var description = ReadText(values, OidSupplyDescription + index);
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            toner.IsAvailable = true;

            var max = ReadLong(values, OidSupplyMaxCapacity + index);
            var level = ReadLong(values, OidSupplyLevel + index);
            if (max is not > 0 || level is null or < 0)
            {
                continue;
            }

            var percent = (int)Math.Clamp(level.Value * 100 / max.Value, 0, 100);
            percents.Add(percent);
            AssignColorPercent(toner, description, percent);
        }

        if (percents.Count > 0)
        {
            toner.State = MapTonerState(percents.Min());
        }

        toner.RawStatus = toner.IsAvailable ? "Supplies reported over SNMP." : string.Empty;
        return toner;
    }

    private static void AssignColorPercent(TonerStatus toner, string description, int percent)
    {
        if (description.Contains("black", StringComparison.OrdinalIgnoreCase) || description.Contains("(k)", StringComparison.OrdinalIgnoreCase))
        {
            toner.BlackPercent = percent;
        }
        else if (description.Contains("cyan", StringComparison.OrdinalIgnoreCase))
        {
            toner.CyanPercent = percent;
        }
        else if (description.Contains("magenta", StringComparison.OrdinalIgnoreCase))
        {
            toner.MagentaPercent = percent;
        }
        else if (description.Contains("yellow", StringComparison.OrdinalIgnoreCase))
        {
            toner.YellowPercent = percent;
        }
        else
        {
            toner.BlackPercent ??= percent;
        }
    }

    private static TonerLevelState MapTonerState(int minPercent) => minPercent switch
    {
        <= 0 => TonerLevelState.Empty,
        <= 5 => TonerLevelState.Critical,
        <= 20 => TonerLevelState.Low,
        _ => TonerLevelState.Ok
    };

    private static string DescribePrinterStatus(long? status) => status switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Idle",
        4 => "Printing",
        5 => "Warming up",
        _ => "Unknown"
    };

    private static string ReadText(IReadOnlyDictionary<string, ISnmpData> values, string oid)
    {
        if (!values.TryGetValue(oid, out var data) || !IsValid(data))
        {
            return string.Empty;
        }

        return data.ToString()?.Trim() ?? string.Empty;
    }

    private static long? ReadLong(IReadOnlyDictionary<string, ISnmpData> values, string oid)
    {
        if (!values.TryGetValue(oid, out var data) || !IsValid(data))
        {
            return null;
        }

        return long.TryParse(data.ToString(), out var number) ? number : null;
    }

    private static bool IsValid(ISnmpData data) =>
        data is not (NoSuchInstance or NoSuchObject or EndOfMibView) && data.TypeCode != SnmpType.Null;

    private static SnmpResult<T> Failure<T>(Exception ex) =>
        new() { Success = false, Message = "SNMP query failed.", TechnicalDetails = ex.Message };
}
