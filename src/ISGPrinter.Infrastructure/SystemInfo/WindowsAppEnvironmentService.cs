using System.Management;
using System.Security.Principal;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.SystemInfo;

public sealed class WindowsAppEnvironmentService : IAppEnvironmentService
{
    public Task<AppEnvironmentInfo> GetEnvironmentAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            var (isDomainJoined, domainOrWorkgroup) = ReadDomainInfo();

            return new AppEnvironmentInfo
            {
                ComputerName = Environment.MachineName,
                UserName = identity.Name,
                DomainName = domainOrWorkgroup,
                IsElevated = principal.IsInRole(WindowsBuiltInRole.Administrator),
                IsDomainJoined = isDomainJoined,
                WindowsVersion = ReadWindowsVersion()
            };
        }, cancellationToken);

    // Win32_ComputerSystem tells the truth about domain membership; Environment
    // .UserDomainName returns the machine name on a workgroup PC.
    private static (bool isDomainJoined, string domainOrWorkgroup) ReadDomainInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PartOfDomain, Domain, Workgroup FROM Win32_ComputerSystem");
            using var system = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            if (system is not null)
            {
                var partOfDomain = system["PartOfDomain"] is bool joined && joined;
                var domain = system["Domain"]?.ToString() ?? string.Empty;
                var workgroup = system["Workgroup"]?.ToString() ?? string.Empty;

                if (partOfDomain && !string.IsNullOrWhiteSpace(domain))
                {
                    return (true, domain);
                }

                var name = !string.IsNullOrWhiteSpace(workgroup)
                    ? workgroup
                    : (!string.IsNullOrWhiteSpace(domain) ? domain : "WORKGROUP");
                return (false, name);
            }
        }
        catch
        {
            // Fall back below.
        }

        return (false, "WORKGROUP");
    }

    // Win32_OperatingSystem.Caption reports "Windows 11" correctly; the kernel
    // version (OSVersion) still says 10.0 on Windows 11 and reads as Windows 10.
    private static string ReadWindowsVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");
            using var os = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            if (os is not null)
            {
                var caption = os["Caption"]?.ToString()?.Trim() ?? string.Empty;
                var build = os["BuildNumber"]?.ToString() ?? string.Empty;

                if (caption.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                {
                    caption = caption["Microsoft ".Length..];
                }

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    return string.IsNullOrWhiteSpace(build) ? caption : $"{caption} (Build {build})";
                }
            }
        }
        catch
        {
            // Fall back below.
        }

        return Environment.OSVersion.VersionString;
    }
}
