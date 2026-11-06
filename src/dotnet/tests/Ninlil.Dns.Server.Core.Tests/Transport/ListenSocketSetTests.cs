using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Tests.Fakes;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class binds real sockets through <see cref="ListenSocketSet" /> on free loopback ports
///     and checks from the client side which addresses receive traffic, which shows a listener
///     bound where it was told to and nowhere else.
/// </summary>
public class ListenSocketSetTests
{
    private static readonly TimeSpan NoReplyWindow = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void NoAddressesConfigured_BindsOneDualStackSocketAsBefore()
    {
        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "",
            0,
            ProtocolType.Udp,
            NullLogger.Instance
        );

        try
        {
            Socket socket = Assert.Single(set.Sockets);
            int port = ((IPEndPoint)socket.LocalEndPoint).Port;
            Assert.Equal(AddressFamily.InterNetworkV6, socket.AddressFamily);
            Assert.True(socket.DualMode);
            Assert.Equal(new IPEndPoint(IPAddress.IPv6Any, port), socket.LocalEndPoint);
            Assert.Empty(set.FailedAddresses);
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void UdpBoundToLoopbackOnly_ReceivesThereButNotOnAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "127.0.0.1",
            0,
            ProtocolType.Udp,
            NullLogger.Instance
        );

        try
        {
            Socket socket = Assert.Single(set.Sockets);
            int port = ((IPEndPoint)socket.LocalEndPoint).Port;
            Assert.Equal(IPAddress.Loopback, ((IPEndPoint)socket.LocalEndPoint).Address);

            SendDatagram(IPAddress.Loopback, port);
            Assert.True(socket.Poll(NoReplyWindow, SelectMode.SelectRead));
            socket.Receive(new byte[16]);

            SendDatagram(other, port);
            Assert.False(socket.Poll(NoReplyWindow, SelectMode.SelectRead));
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public async Task TcpBoundToLoopbackOnly_AcceptsThereButRefusesAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "127.0.0.1",
            0,
            ProtocolType.Tcp,
            NullLogger.Instance
        );

        try
        {
            int port = ((IPEndPoint)Assert.Single(set.Sockets).LocalEndPoint).Port;
            using (TcpClient client = new(AddressFamily.InterNetwork))
                await client.ConnectAsync(IPAddress.Loopback, port);

            using TcpClient refused = new(other.AddressFamily);
            await Assert.ThrowsAsync<SocketException>(() => refused.ConnectAsync(other, port));
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void BothWildcards_BindSideBySideOnTheSamePort()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        (ListenSocketSet set, int _) = BindOnFreePort(
            port =>
                ListenSocketSet.Bind(
                    "test",
                    "0.0.0.0, ::",
                    port,
                    ProtocolType.Udp,
                    NullLogger.Instance
                ),
            expectedSockets: 2
        );

        try
        {
            Assert.Equal(2, set.Sockets.Count);
            Assert.Empty(set.FailedAddresses);

            // The IPv6 socket must be IPv6-only, or it would have claimed IPv4 too and the two
            // binds would have collided.
            Socket ipv6 = set.Sockets.Single(socket =>
                socket.AddressFamily == AddressFamily.InterNetworkV6
            );
            Assert.False(ipv6.DualMode);
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void AnUnbindableAndAMalformedAddress_AreLoggedAndReportedWhileTheRestServe()
    {
        CapturingLogger<ListenSocketSetTests> logger = new();
        (ListenSocketSet set, int port) = BindOnFreePort(
            port =>
                ListenSocketSet.Bind(
                    "Plain DNS (UDP)",
                    $"127.0.0.1, {LocalAddresses.Unassigned}, eth0",
                    port,
                    ProtocolType.Udp,
                    logger
                ),
            expectedSockets: 1
        );

        try
        {
            Socket socket = Assert.Single(set.Sockets);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), socket.LocalEndPoint);

            Assert.Equal(2, set.FailedAddresses.Count);
            Assert.Contains(set.FailedAddresses, failed => failed.StartsWith("192.0.2.1 ("));
            Assert.Contains("eth0 (not an IP address)", set.FailedAddresses);

            Assert.Contains(
                logger.Errors,
                message =>
                    message.Contains("Plain DNS (UDP)")
                    && message.Contains("192.0.2.1")
                    && message.Contains($"port {port}")
            );
            Assert.Contains(
                logger.Errors,
                message => message.Contains("Plain DNS (UDP)") && message.Contains("'eth0'")
            );

            SendDatagram(IPAddress.Loopback, port);
            Assert.True(socket.Poll(NoReplyWindow, SelectMode.SelectRead));
        }
        finally
        {
            set.Close();
        }
    }

    [Theory]
    [InlineData(ProtocolType.Udp)]
    [InlineData(ProtocolType.Tcp)]
    public void NoAddressesConfigured_WhenThePortIsTaken_BindsNothingReportsItAndDoesNotThrow(
        ProtocolType protocolType
    )
    {
        // The occupant holds the same wildcard address and port without address reuse, which is
        // what another program (or a second Ninlil) listening there looks like to the bind.
        CapturingLogger<ListenSocketSetTests> logger = new();
        Socket occupant = new(
            AddressFamily.InterNetworkV6,
            protocolType == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram,
            protocolType
        );
        occupant.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        occupant.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        // A bound but not listening TCP socket does not block a second bind on Unix.
        if (protocolType == ProtocolType.Tcp)
            occupant.Listen();

        try
        {
            ListenSocketSet set = ListenSocketSet.Bind(
                "Plain DNS (test)",
                "",
                port,
                protocolType,
                logger
            );

            Assert.Empty(set.Sockets);
            string failed = Assert.Single(set.FailedAddresses);
            Assert.StartsWith("::", failed);
            Assert.Contains(
                logger.Errors,
                message =>
                    message.Contains("Plain DNS (test)")
                    && message.Contains("::")
                    && message.Contains($"port {port}")
                    && PortTakenReason.IsIn(message)
            );
        }
        finally
        {
            occupant.Dispose();
        }
    }

    [Fact]
    public void NoUsableAddress_BindsNothingAndSaysSo()
    {
        CapturingLogger<ListenSocketSetTests> logger = new();
        ListenSocketSet set = ListenSocketSet.Bind(
            "DNS-over-TLS",
            LocalAddresses.Unassigned.ToString(),
            0,
            ProtocolType.Tcp,
            logger
        );

        Assert.Empty(set.Sockets);
        Assert.Single(set.FailedAddresses);
        Assert.Contains(logger.Errors, message => message.Contains("not listening anywhere"));
    }

    [Fact]
    public void AnAddressCoveredByItsWildcard_IsNotBoundTwice()
    {
        CapturingLogger<ListenSocketSetTests> logger = new();
        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "0.0.0.0, 127.0.0.1",
            0,
            ProtocolType.Tcp,
            logger
        );

        try
        {
            Assert.Single(set.Sockets);
            Assert.Empty(set.FailedAddresses);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning && entry.Message.Contains("127.0.0.1")
            );
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void ResolveEndpoints_FollowsTheSameRules()
    {
        IReadOnlyList<IPEndPoint> everywhere = ListenSocketSet.ResolveEndpoints(
            "test",
            "",
            853,
            NullLogger.Instance,
            out List<string> noFailures
        );
        Assert.Equal([new IPEndPoint(IPAddress.IPv6Any, 853)], everywhere);
        Assert.Empty(noFailures);

        IReadOnlyList<IPEndPoint> listed = ListenSocketSet.ResolveEndpoints(
            "test",
            "127.0.0.1, bogus, ::1",
            853,
            NullLogger.Instance,
            out List<string> failures
        );
        Assert.Equal(
            [new IPEndPoint(IPAddress.Loopback, 853), new IPEndPoint(IPAddress.IPv6Loopback, 853)],
            listed
        );
        Assert.Equal(["bogus (not an IP address)"], failures);
    }

    [Fact]
    public void IsAssignedToThisHost_KnowsLoopbackFromAnUnassignedAddress()
    {
        Assert.True(ListenSocketSet.IsAssignedToThisHost(IPAddress.Loopback));
        Assert.True(ListenSocketSet.IsAssignedToThisHost(IPAddress.Any));
        Assert.True(ListenSocketSet.IsAssignedToThisHost(IPAddress.IPv6Any));
        Assert.False(ListenSocketSet.IsAssignedToThisHost(LocalAddresses.Unassigned));
    }

    /// <summary>
    ///     This method binds a set whose sockets must share one known port. Port 0 would give each
    ///     socket its own port, so the method picks a free port first and retries a bounded number
    ///     of times when another program takes it before the bind, which shows as fewer sockets
    ///     than <paramref name="expectedSockets" />.
    /// </summary>
    private static (ListenSocketSet Set, int Port) BindOnFreePort(
        Func<int, ListenSocketSet> bind,
        int expectedSockets
    )
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = LocalAddresses.GetFreePort();
            ListenSocketSet set = bind(port);

            if (set.Sockets.Count == expectedSockets || attempt == 4)
                return (set, port);

            set.Close();
        }
    }

    private static void SendDatagram(IPAddress address, int port)
    {
        using Socket sender = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        sender.SendTo([1, 2, 3], new IPEndPoint(address, port));
    }
}
