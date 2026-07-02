using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Diagnostics;

public sealed class NetworkProbeProvider : INetworkProbeProvider
{
    public async Task<NetworkProbeResult> ResolveDnsAsync(string hostName, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostName, cancellationToken);
            watch.Stop();

            return new NetworkProbeResult
            {
                Success = addresses.Length > 0,
                Target = hostName,
                Elapsed = watch.Elapsed,
                Message = addresses.Length > 0 ? "DNS resolved." : "DNS returned no addresses."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            return new NetworkProbeResult
            {
                Success = false,
                Target = hostName,
                Elapsed = watch.Elapsed,
                Message = "DNS resolve failed.",
                TechnicalDetails = ex.Message
            };
        }
    }

    public async Task<NetworkProbeResult> PingAsync(string hostNameOrIpAddress, int timeoutMs, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostNameOrIpAddress, timeoutMs).WaitAsync(cancellationToken);
            watch.Stop();

            return new NetworkProbeResult
            {
                Success = reply.Status == IPStatus.Success,
                Target = hostNameOrIpAddress,
                Elapsed = watch.Elapsed,
                Message = reply.Status == IPStatus.Success ? "Ping succeeded." : $"Ping status: {reply.Status}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            return new NetworkProbeResult
            {
                Success = false,
                Target = hostNameOrIpAddress,
                Elapsed = watch.Elapsed,
                Message = "Ping failed.",
                TechnicalDetails = ex.Message
            };
        }
    }

    public async Task<NetworkProbeResult> CheckTcpPortAsync(string hostNameOrIpAddress, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();

        // A linked deadline cancels the connect itself on timeout, so the socket
        // is torn down cleanly. The old WhenAny(connectTask, Delay) approach left
        // the connect running and faulting unobserved — a real problem when the
        // subnet scanner fires hundreds of these against dead hosts.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(hostNameOrIpAddress, port, timeoutCts.Token);

            watch.Stop();

            return new NetworkProbeResult
            {
                Success = client.Connected,
                Target = hostNameOrIpAddress,
                Port = port,
                Elapsed = watch.Elapsed,
                Message = client.Connected ? $"TCP {port} reachable." : $"TCP {port} not reachable."
            };
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            watch.Stop();
            return new NetworkProbeResult
            {
                Success = false,
                Target = hostNameOrIpAddress,
                Port = port,
                Elapsed = watch.Elapsed,
                Message = $"TCP {port} timed out."
            };
        }
        catch (Exception ex)
        {
            watch.Stop();
            return new NetworkProbeResult
            {
                Success = false,
                Target = hostNameOrIpAddress,
                Port = port,
                Elapsed = watch.Elapsed,
                Message = $"TCP {port} check failed.",
                TechnicalDetails = ex.Message
            };
        }
    }
}
