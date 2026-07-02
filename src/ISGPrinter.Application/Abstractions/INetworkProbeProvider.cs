using ISGPrinter.Domain.Models;

namespace ISGPrinter.Application.Abstractions;

public interface INetworkProbeProvider
{
    Task<NetworkProbeResult> ResolveDnsAsync(string hostName, CancellationToken cancellationToken);

    Task<NetworkProbeResult> PingAsync(string hostNameOrIpAddress, int timeoutMs, CancellationToken cancellationToken);

    Task<NetworkProbeResult> CheckTcpPortAsync(string hostNameOrIpAddress, int port, int timeoutMs, CancellationToken cancellationToken);
}
