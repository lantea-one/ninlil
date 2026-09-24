// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class covers <see cref="DnsOverHttpsTransportGate" />'s own start/stop signal in
///     isolation -- the real end-to-end proof that a stopped gate actually makes a mapped
///     DNS-over-HTTPS request fail lives in <c>Ninlil.Web.Tests</c>' own
///     <c>ServicesControllerCrudTests</c>, against the real shared-Kestrel-mapped endpoint.
/// </summary>
public sealed class DnsOverHttpsTransportGateTests
{
    [Fact]
    public void AFreshGate_ReportsNotRunning()
    {
        DnsOverHttpsTransportGate gate = new();

        Assert.False(gate.IsRunning);
    }

    [Fact]
    public async Task StartAsync_OpensTheGate()
    {
        DnsOverHttpsTransportGate gate = new();

        await gate.StartAsync(CancellationToken.None);

        Assert.True(gate.IsRunning);
    }

    [Fact]
    public async Task StopAsync_AfterStart_ClosesTheGateAgain()
    {
        DnsOverHttpsTransportGate gate = new();
        await gate.StartAsync(CancellationToken.None);

        await gate.StopAsync(CancellationToken.None);

        Assert.False(gate.IsRunning);
    }
}
