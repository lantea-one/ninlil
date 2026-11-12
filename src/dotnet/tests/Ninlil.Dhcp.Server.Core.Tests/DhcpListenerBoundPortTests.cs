using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Tests.Fakes;
// The alias is needed for the reason given in Dhcpv4UdpRoundTripTests.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers <c>BoundPort</c> on <see cref="Dhcpv4UdpListener" /> and
///     <see cref="Dhcpv6UdpListener" />: with the server port set to 0 the operating system picks
///     the port, and the listener reports it, so a test never has to reserve a port number and
///     release it before the bind. It uses real loopback sockets and handlers that never reply.
/// </summary>
public sealed class DhcpListenerBoundPortTests
{
    [Fact]
    public async Task Dhcpv4BoundPort_WithServerPortZero_IsTheBoundPortAndIsZeroAgainAfterAStop()
    {
        Dhcpv4UdpListener listener = new(
            new SilentDhcpv4Handler(),
            MicrosoftOptions.Create(new Dhcpv4ProtocolOptions { Enable = true, ServerPort = 0 }),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        Assert.Equal(0, listener.BoundPort);

        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.InRange(listener.BoundPort, 1, 65535);

            // The reported port is the one that is taken: a second wildcard bind without
            // address reuse cannot have it.
            using Socket probe = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            Assert.Throws<SocketException>(() =>
                probe.Bind(new IPEndPoint(IPAddress.Any, listener.BoundPort))
            );
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, listener.BoundPort);
    }

    [Fact]
    public async Task Dhcpv6BoundPort_WithServerPortZero_IsTheBoundPortAndIsZeroAgainAfterAStop()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        Dhcpv6UdpListener listener = new(
            new SilentDhcpv6Handler(),
            MicrosoftOptions.Create(new Dhcpv6ProtocolOptions { Enable = true, ServerPort = 0 }),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        Assert.Equal(0, listener.BoundPort);

        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.InRange(listener.BoundPort, 1, 65535);

            // The reported port is the one that is taken: a second wildcard bind without
            // address reuse cannot have it.
            using Socket probe = new(
                AddressFamily.InterNetworkV6,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            Assert.Throws<SocketException>(() =>
                probe.Bind(new IPEndPoint(IPAddress.IPv6Any, listener.BoundPort))
            );
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, listener.BoundPort);
    }

    /// <summary>
    ///     This handler answers no DHCPv4 request; the tests only look at the bound socket.
    /// </summary>
    private sealed class SilentDhcpv4Handler : IDhcpv4RequestHandler
    {
        /// <inheritdoc />
        public ValueTask<Dhcpv4Message> HandleRequestAsync(
            Dhcpv4Message request,
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<Dhcpv4Message>(null);
    }

    /// <summary>
    ///     This handler answers no DHCPv6 request; the tests only look at the bound socket.
    /// </summary>
    private sealed class SilentDhcpv6Handler : IDhcpv6RequestHandler
    {
        /// <inheritdoc />
        public ValueTask<Dhcpv6Message> HandleRequestAsync(
            Dhcpv6Message request,
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<Dhcpv6Message>(null);
    }
}
