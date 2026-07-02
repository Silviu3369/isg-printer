using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Tests;

public sealed class DomainDefaultsTests
{
    [Fact]
    public void AppSettings_Defaults_AreProductionSafe()
    {
        var settings = new AppSettings();

        Assert.Equal("ISG Printer", settings.AppName);
        Assert.True(settings.EnableSnmp);
        Assert.False(settings.EnableIpRangeScan);
        Assert.Equal("Default", settings.DefaultSnmpProfile);
        Assert.Contains(9100, settings.TcpPortsToCheck);
        Assert.Contains(631, settings.TcpPortsToCheck);
    }

    [Fact]
    public void OperationResult_Ok_ReturnsSuccessStatus()
    {
        var result = OperationResult.Ok("Done");

        Assert.True(result.Success);
        Assert.Equal(OperationStatus.Success, result.Status);
        Assert.Equal("Done", result.Message);
    }

    [Fact]
    public void SnmpProfile_DefaultV2_DoesNotStorePlainSecret()
    {
        var profile = SnmpProfile.DefaultV2();

        Assert.Equal(SnmpVersion.V2C, profile.Version);
        Assert.Equal("snmp-default-community", profile.CommunitySecretName);
        Assert.DoesNotContain("public", profile.CommunitySecretName, StringComparison.OrdinalIgnoreCase);
    }
}
