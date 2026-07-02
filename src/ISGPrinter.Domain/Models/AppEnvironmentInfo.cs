namespace ISGPrinter.Domain.Models;

public sealed class AppEnvironmentInfo
{
    public string ComputerName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string DomainName { get; set; } = string.Empty;

    public bool IsElevated { get; set; }

    public bool IsDomainJoined { get; set; }

    public string WindowsVersion { get; set; } = string.Empty;
}
