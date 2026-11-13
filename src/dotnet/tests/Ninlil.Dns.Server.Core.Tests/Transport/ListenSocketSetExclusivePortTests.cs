using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Tests.Fakes;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class checks on Windows that <see cref="ListenSocketSet" /> claims its port
///     exclusively, with real sockets on OS-assigned ports. Every test returns at once on other
///     systems, where the sockets behave as before.
///     <para>
///         The dual-stack tests name combinations of sockets that Windows allows on one port
///         without exclusive use (measured on a Windows host, and described in the Winsock
///         documentation "Using SO_REUSEADDR and SO_EXCLUSIVEADDRUSE"), and prove the bind is
///         refused. The explicit-address tests guard behaviour Windows gives with or without the
///         claim: the same address stays refused, and two different addresses on one port keep
///         working.
///     </para>
///     These tests cover the plain, DNS-over-TLS and DNSCrypt listeners, which all bind through
///     this class.
/// </summary>
public class ListenSocketSetExclusivePortTests
{
    [Theory]
    [InlineData(ProtocolType.Udp)]
    [InlineData(ProtocolType.Tcp)]
    public void DualStackSet_OnWindows_RefusesASpecificAddressSocketOnTheSamePort(
        ProtocolType protocolType
    )
    {
        if (!OperatingSystem.IsWindows())
            return;

        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "",
            0,
            protocolType,
            NullLogger.Instance
        );

        try
        {
            Assert.Single(set.Sockets);
            int port = set.BoundPort;

            // Without exclusive use Windows lets this bind and sends 127.0.0.1 traffic to it.
            using Socket specific = CreateSocket(AddressFamily.InterNetwork, protocolType);
            Assert.Throws<SocketException>(() =>
                specific.Bind(new IPEndPoint(IPAddress.Loopback, port))
            );

            // Another wildcard socket with SO_REUSEADDR hijacks the port without exclusive use.
            using Socket reusing = CreateSocket(AddressFamily.InterNetworkV6, protocolType);
            reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() =>
                reusing.Bind(new IPEndPoint(IPAddress.IPv6Any, port))
            );
        }
        finally
        {
            set.Close();
        }
    }

    [Theory]
    [InlineData(ProtocolType.Udp)]
    [InlineData(ProtocolType.Tcp)]
    public void DualStackSet_OnWindows_WhenASpecificAddressSocketHoldsThePort_FailsAndIsReported(
        ProtocolType protocolType
    )
    {
        if (!OperatingSystem.IsWindows())
            return;

        // The other order: the specific-address socket is up first. Without exclusive use the
        // wildcard bind succeeds beside it and the set never sees that address's clients.
        using Socket occupant = CreateSocket(AddressFamily.InterNetwork, protocolType);
        occupant.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "",
            port,
            protocolType,
            NullLogger.Instance
        );

        try
        {
            Assert.Empty(set.Sockets);
            Assert.Single(set.FailedAddresses);
        }
        finally
        {
            set.Close();
        }
    }

    [Theory]
    [InlineData(ProtocolType.Udp)]
    [InlineData(ProtocolType.Tcp)]
    public void ExplicitAddressSet_OnWindows_RefusesAReusingSocketOnTheSameAddressAndPort(
        ProtocolType protocolType
    )
    {
        if (!OperatingSystem.IsWindows())
            return;

        ListenSocketSet set = ListenSocketSet.Bind(
            "test",
            "127.0.0.1",
            0,
            protocolType,
            NullLogger.Instance
        );

        try
        {
            Assert.Single(set.Sockets);

            // A guard, not a test of the claim: Windows refuses this bind with or without it.
            // Exclusive use is per address: a socket on the same address is refused even with
            // SO_REUSEADDR, while a wildcard socket would not take traffic the specific address
            // receives, so Windows allows that bind and this test does not ask for it.
            using Socket reusing = CreateSocket(AddressFamily.InterNetwork, protocolType);
            reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() =>
                reusing.Bind(new IPEndPoint(IPAddress.Loopback, set.BoundPort))
            );
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void DualStackSet_OnWindows_AfterClose_LetsTheSamePortBeBoundAgain()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Exclusive use must not stop a restart: the port is free again once the set is closed.
        ListenSocketSet first = ListenSocketSet.Bind(
            "test",
            "",
            0,
            ProtocolType.Udp,
            NullLogger.Instance
        );
        int port = first.BoundPort;
        first.Close();

        ListenSocketSet second = ListenSocketSet.Bind(
            "test",
            "",
            port,
            ProtocolType.Udp,
            NullLogger.Instance
        );

        try
        {
            Assert.Single(second.Sockets);
            Assert.Empty(second.FailedAddresses);
        }
        finally
        {
            second.Close();
        }
    }

    [Fact]
    public void ExplicitAddressSet_OnEverySystem_StillBindsTwoAddressesOnTheSamePort()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        // Exclusive use is per address on Windows (Winsock documentation): two sets that name
        // different addresses of one port keep working, which is what a multi-address
        // deployment relies on. The first set picks the port, the second joins it.
        ListenSocketSet first = ListenSocketSet.Bind(
            "test",
            "127.0.0.1",
            0,
            ProtocolType.Udp,
            NullLogger.Instance
        );

        try
        {
            ListenSocketSet second = ListenSocketSet.Bind(
                "test",
                other.ToString(),
                first.BoundPort,
                ProtocolType.Udp,
                NullLogger.Instance
            );

            try
            {
                Assert.Single(second.Sockets);
                Assert.Empty(second.FailedAddresses);
            }
            finally
            {
                second.Close();
            }
        }
        finally
        {
            first.Close();
        }
    }

    /// <summary>This method creates an unbound socket of the family and protocol under test.</summary>
    private static Socket CreateSocket(AddressFamily family, ProtocolType protocolType) =>
        new(
            family,
            protocolType == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram,
            protocolType
        );
}
