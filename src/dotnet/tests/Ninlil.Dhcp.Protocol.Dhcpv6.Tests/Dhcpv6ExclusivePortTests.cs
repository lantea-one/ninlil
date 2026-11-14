using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test class checks on Windows that <see cref="Dhcpv6UdpListener" /> claims its UDP port
///     exclusively, so no other socket can share the DHCPv6 server port: a wildcard socket with
///     SO_REUSEADDR is refused (Winsock documentation "Using SO_REUSEADDR and
///     SO_EXCLUSIVEADDRUSE"). The test returns at once on other systems and where IPv6 is
///     missing.
/// </summary>
public class Dhcpv6ExclusivePortTests
{
    [Fact]
    public async Task StartAsync_OnWindows_RefusesAReusingSocketOnTheServerPort()
    {
        if (!OperatingSystem.IsWindows() || !LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        Dhcpv6ProtocolOptions options = new() { Enable = true, ServerPort = 0 };
        Dhcpv6UdpListener listener = new(
            new StubDhcpv6RequestHandler(),
            Options.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(listener.IsListening);

            using Socket reusing = new(
                AddressFamily.InterNetworkV6,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() =>
                reusing.Bind(new IPEndPoint(IPAddress.IPv6Any, listener.BoundPort))
            );
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }
}
