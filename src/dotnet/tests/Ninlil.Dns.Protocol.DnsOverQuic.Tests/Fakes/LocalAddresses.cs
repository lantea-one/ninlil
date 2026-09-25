using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic.Tests.Fakes;

/// <summary>
///     This class finds free ports and usable loopback addresses for the listen-address tests,
///     which use real sockets. Ports always come from the OS, never a fixed number, because CI
///     runners run DNS and DHCP services of their own.
/// </summary>
public static class LocalAddresses
{
    /// <summary>
    ///     This property contains an address from the TEST-NET-1 documentation range (RFC 5737),
    ///     which is not assigned to any host, so binding it fails with "cannot assign requested
    ///     address".
    /// </summary>
    public static IPAddress Unassigned { get; } = IPAddress.Parse("192.0.2.1");

    /// <summary>
    ///     This method returns a port that is free for both UDP and TCP on every address, by
    ///     briefly holding it through dual-stack wildcard sockets.
    /// </summary>
    public static int GetFreePort()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using Socket udp = CreateWildcardSocket(SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(WildcardAddress, 0));
            int port = ((IPEndPoint)udp.LocalEndPoint).Port;

            using Socket tcp = CreateWildcardSocket(SocketType.Stream, ProtocolType.Tcp);

            try
            {
                tcp.Bind(new IPEndPoint(WildcardAddress, port));
                return port;
            }
            catch (SocketException)
            {
                // Taken for TCP by something else; try another.
            }
        }

        throw new InvalidOperationException("Could not find a port free for both UDP and TCP.");
    }

    /// <summary>
    ///     This method returns a second loopback address distinct from 127.0.0.1 that this host can
    ///     bind: <c>::1</c> when IPv6 is available, otherwise <c>127.0.0.2</c> (Linux routes all of
    ///     127.0.0.0/8 to loopback), or <see langword="null" /> when neither works.
    /// </summary>
    public static IPAddress OtherLoopback() =>
        CanBind(IPAddress.IPv6Loopback) ? IPAddress.IPv6Loopback
        : CanBind(IPAddress.Parse("127.0.0.2")) ? IPAddress.Parse("127.0.0.2")
        : null;

    /// <summary>This method reports whether a UDP socket can be bound to an address.</summary>
    public static bool CanBind(IPAddress address)
    {
        try
        {
            using Socket socket = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(address, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static IPAddress WildcardAddress =>
        Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;

    private static Socket CreateWildcardSocket(SocketType socketType, ProtocolType protocolType)
    {
        Socket socket = new(WildcardAddress.AddressFamily, socketType, protocolType);

        if (WildcardAddress.AddressFamily == AddressFamily.InterNetworkV6)
            socket.DualMode = true;

        return socket;
    }
}
