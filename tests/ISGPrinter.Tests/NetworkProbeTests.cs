using ISGPrinter.Infrastructure.Diagnostics;

namespace ISGPrinter.Tests;

public sealed class NetworkProbeTests
{
    [Fact]
    public async Task CheckTcpPort_UnreachableHost_ReturnsResultWithoutHangingOrThrowing()
    {
        var provider = new NetworkProbeProvider();

        // RFC 5737 TEST-NET-1 — never routes or answers. With a short budget the
        // probe must return a clean failed result quickly: never hang, never throw.
        // (Regression guard for the linked-deadline rewrite that replaced the
        // abandoned WhenAny(connect, delay) connect task.)
        var probe = await provider
            .CheckTcpPortAsync("192.0.2.1", 9100, 300, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(probe.Success);
        Assert.Equal(9100, probe.Port);
    }

    [Fact]
    public async Task CheckTcpPort_AlreadyCancelledToken_PropagatesCancellation()
    {
        var provider = new NetworkProbeProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider
            .CheckTcpPortAsync("192.0.2.1", 9100, 5000, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
