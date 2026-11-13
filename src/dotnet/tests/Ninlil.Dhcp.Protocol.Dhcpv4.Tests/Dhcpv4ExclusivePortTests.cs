using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks on Windows that <see cref="Dhcpv4UdpListener" /> claims its UDP port
///     exclusively, so no other socket can share the DHCPv4 server port. It starts the listener on
///     an OS-assigned port and tries the combinations Windows allows without exclusive use (a
///     specific address beside the wildcard, and a wildcard with SO_REUSEADDR; Winsock
///     documentation "Using SO_REUSEADDR and SO_EXCLUSIVEADDRUSE"). Both are refused. The tests
///     return at once on other systems.
/// </summary>
public class Dhcpv4ExclusivePortTests
{
    [Fact]
    public async Task StartAsync_OnWindows_RefusesEverySecondBindOfTheServerPort()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Dhcpv4ProtocolOptions options = new() { Enable = true, ServerPort = 0 };
        Dhcpv4UdpListener listener = new(
            new StubDhcpv4RequestHandler(),
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(listener.IsListening);
            int port = listener.BoundPort;

            using Socket specific = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            Assert.Throws<SocketException>(() =>
                specific.Bind(new IPEndPoint(IPAddress.Loopback, port))
            );

            using Socket reusing = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() => reusing.Bind(new IPEndPoint(IPAddress.Any, port)));
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_OnWindows_AfterStop_BindsTheSamePortAgain()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // A stop and start from the service control API must keep working with exclusive use.
        Dhcpv4ProtocolOptions options = new() { Enable = true, ServerPort = 0 };
        Dhcpv4UdpListener first = new(
            new StubDhcpv4RequestHandler(),
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );
        await first.StartAsync(CancellationToken.None);
        int port = first.BoundPort;
        await first.StopAsync(CancellationToken.None);

        Dhcpv4UdpListener second = new(
            new StubDhcpv4RequestHandler(),
            Options.Create(new Dhcpv4ProtocolOptions { Enable = true, ServerPort = port }),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        try
        {
            await second.StartAsync(CancellationToken.None);
            Assert.True(second.IsListening);
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }
    }
}
