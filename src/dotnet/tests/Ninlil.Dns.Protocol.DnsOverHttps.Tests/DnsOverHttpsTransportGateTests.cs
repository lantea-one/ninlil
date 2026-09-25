// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class tests the start/stop flag of <see cref="DnsOverHttpsTransportGate" /> on its
///     own. The effect of the gate on requests to the mapped endpoint is tested in
///     <c>Ninlil.Web.Tests</c> (<c>TransportSettingsControllerTests</c>).
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
