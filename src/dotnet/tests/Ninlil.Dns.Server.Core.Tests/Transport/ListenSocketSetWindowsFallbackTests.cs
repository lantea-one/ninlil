using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class checks on Windows the per-address fallback of <see cref="ListenSocketSet" />
///     in listen-everywhere mode, with real UDP sockets on OS-assigned ports. A foreign socket
///     that does not claim its port exclusively holds the IPv4 wildcard address, like the DNS
///     proxy of connection sharing on UDP 53. Ninlil's exclusive wildcard bind then fails, and
///     the set binds each local address on its own. Every test returns at once on other systems,
///     which have no fallback.
///     <para>
///         The foreign socket binds port 0 first and the set then binds that same port. This is
///         not a reserve-and-release: the foreign socket stays open for the whole test, so the
///         port cannot be taken by anyone else in between.
///     </para>
/// </summary>
public class ListenSocketSetWindowsFallbackTests
{
    [Fact]
    public void Bind_OnWindows_WhenAForeignSocketHoldsTheWildcard_BindsEachLocalAddressInstead()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using Socket foreign = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        foreign.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)foreign.LocalEndPoint).Port;
        RecordingLogger logger = new();

        ListenSocketSet set = ListenSocketSet.Bind("test", "", port, ProtocolType.Udp, logger);

        try
        {
            Assert.NotEmpty(set.Sockets);
            Assert.All(
                set.Sockets,
                socket =>
                {
                    IPEndPoint endPoint = (IPEndPoint)socket.LocalEndPoint;
                    Assert.Equal(port, endPoint.Port);
                    Assert.False(endPoint.Address.Equals(IPAddress.Any));
                    Assert.False(endPoint.Address.Equals(IPAddress.IPv6Any));
                }
            );

            Socket loopback = Assert.Single(
                set.Sockets,
                socket => ((IPEndPoint)socket.LocalEndPoint).Address.Equals(IPAddress.Loopback)
            );

            // A query to 127.0.0.1 reaches Ninlil's socket, not the foreign wildcard socket:
            // the more specific bind receives the datagram.
            using Socket client = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            client.SendTo([1, 2, 3], new IPEndPoint(IPAddress.Loopback, port));
            Assert.True(loopback.Poll(5_000_000, SelectMode.SelectRead));
            Assert.False(foreign.Poll(0, SelectMode.SelectRead));

            // The claim is exclusive: nobody else can take 127.0.0.1 on that port.
            using Socket second = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            second.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() =>
                second.Bind(new IPEndPoint(IPAddress.Loopback, port))
            );

            Assert.Single(logger.Warnings);
            Assert.Contains("UDP port " + port, logger.Warnings[0]);
        }
        finally
        {
            set.Close();
        }
    }

    [Fact]
    public void Bind_OnWindows_WhenNothingHoldsThePort_StaysOneWildcardSocket()
    {
        if (!OperatingSystem.IsWindows())
            return;

        RecordingLogger logger = new();

        ListenSocketSet set = ListenSocketSet.Bind("test", "", 0, ProtocolType.Udp, logger);

        try
        {
            Socket socket = Assert.Single(set.Sockets);
            Assert.Equal(IPAddress.IPv6Any, ((IPEndPoint)socket.LocalEndPoint).Address);
            Assert.Empty(set.FailedAddresses);
            Assert.Empty(logger.Warnings);
        }
        finally
        {
            set.Close();
        }
    }

    /// <summary>
    ///     This logger keeps the text of every warning, so a test can check that the fallback
    ///     warns once and names the port and protocol.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        /// <summary>This property contains the formatted text of every warning logged.</summary>
        public List<string> Warnings { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
