using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks what <see cref="Dhcpv4ListenerHostedService" /> does at boot when
///     the DHCPv4 port is already taken: another socket on the wildcard UDP address holds an
///     OS-assigned port (without address reuse), so the listener's bind fails with a real
///     "address in use" error. The hosted service must not throw, because an exception out of a
///     hosted service stops the whole host. It logs one Error, the listener stays stopped and
///     keeps the reason for the health check.
/// </summary>
public class Dhcpv4ListenerBindFailureTests
{
    [Fact]
    public async Task HostedServiceStart_WhenThePortIsTaken_LogsOneErrorAndDoesNotThrow()
    {
        using Socket occupant = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupant.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        Dhcpv4ProtocolOptions options = new() { Enable = true, ServerPort = port };
        Dhcpv4UdpListener listener = new(
            new StubDhcpv4RequestHandler(),
            Options.Create(options),
            new CapturingLogger<Dhcpv4UdpListener>()
        );
        CapturingLogger<Dhcpv4ListenerHostedService> logger = new();
        Dhcpv4ListenerHostedService hosted = new(listener, Options.Create(options), logger);

        await hosted.StartAsync(CancellationToken.None);

        Assert.False(listener.IsListening);
        Assert.Contains("in use", listener.StartFailure, StringComparison.OrdinalIgnoreCase);
        string error = Assert.Single(logger.Messages(LogLevel.Error));
        Assert.Contains("could not bind its socket", error);
        Assert.Contains("in use", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostedServiceStart_WhenThePortIsFree_ClearsTheStartFailure()
    {
        using Socket occupant = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupant.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;
        Dhcpv4ProtocolOptions options = new() { Enable = true, ServerPort = port };
        Dhcpv4UdpListener listener = new(
            new StubDhcpv4RequestHandler(),
            Options.Create(options),
            new CapturingLogger<Dhcpv4UdpListener>()
        );
        await new Dhcpv4ListenerHostedService(
            listener,
            Options.Create(options),
            new CapturingLogger<Dhcpv4ListenerHostedService>()
        ).StartAsync(CancellationToken.None);
        Assert.NotNull(listener.StartFailure);

        // Free the port, then start again the way the service control API does.
        occupant.Dispose();
        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(listener.IsListening);
            Assert.Null(listener.StartFailure);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }
}
