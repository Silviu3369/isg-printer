using ISGPrinter.Domain.Enums;

namespace ISGPrinter.Domain.Models;

public sealed class SnmpProfile
{
    public string Name { get; set; } = "Default";

    public SnmpVersion Version { get; set; } = SnmpVersion.V2C;

    public string CommunitySecretName { get; set; } = "snmp-default-community";

    public string UserName { get; set; } = string.Empty;

    public string AuthSecretName { get; set; } = string.Empty;

    public string PrivacySecretName { get; set; } = string.Empty;

    public SnmpAuthenticationProtocol AuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.None;

    public SnmpPrivacyProtocol PrivacyProtocol { get; set; } = SnmpPrivacyProtocol.None;

    public static SnmpProfile DefaultV2() =>
        new()
        {
            Name = "Default",
            Version = SnmpVersion.V2C,
            CommunitySecretName = "snmp-default-community"
        };
}
