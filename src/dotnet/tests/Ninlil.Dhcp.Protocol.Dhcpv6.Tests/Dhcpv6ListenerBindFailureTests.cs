using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test class checks what <see cref="Dhcpv6ListenerHostedService" /> does at boot when
///     the DHCPv6 port is already taken: another socket on the IPv6 wildcard UDP address holds an
///     OS-assigned port (without address reuse), so the listener's bind fails with a real
///     "address in use" error. The hosted service must not throw, because an exception out of a
///     hosted service stops the whole host. It logs one Error and the listener stays stopped and
///     keeps the reason for the health check.
/// </summary>
public class Dhcpv6ListenerBindFailureTests
{
    [Fact]
    public async Task HostedServiceStart_WhenThePortIsTaken_LogsOneErrorAndDoesNotThrow()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        using Socket occupant = new(
            AddressFamily.InterNetworkV6,
            SocketType.Dgram,
            ProtocolType.Udp
        );
        occupant.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        Dhcpv6ProtocolOptions options = new() { Enable = true, ServerPort = port };
        Dhcpv6UdpListener listener = new(
            new StubDhcpv6RequestHandler(),
            Options.Create(options),
            new CapturingLogger<Dhcpv6UdpListener>()
        );
        CapturingLogger<Dhcpv6ListenerHostedService> logger = new();
        Dhcpv6ListenerHostedService hosted = new(listener, Options.Create(options), logger);

        await hosted.StartAsync(CancellationToken.None);

        Assert.False(listener.IsListening);
        Assert.Contains("in use", listener.StartFailure, StringComparison.OrdinalIgnoreCase);
        string error = Assert.Single(logger.Messages(LogLevel.Error));
        Assert.Contains("could not bind its socket", error);
        Assert.Contains("in use", error, StringComparison.OrdinalIgnoreCase);
    }
}
