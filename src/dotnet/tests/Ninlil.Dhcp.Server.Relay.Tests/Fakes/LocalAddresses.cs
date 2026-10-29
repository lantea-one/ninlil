using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests.Fakes;

/// <summary>
///     This class tells the live DHCPv6 tests of this project whether the host can bind a UDP
///     socket to the IPv6 loopback address. A test that needs <c>::1</c> returns early when it
///     cannot (for example in a container with IPv6 disabled) instead of failing with a
///     <see cref="SocketException" />. <see cref="Socket.OSSupportsIPv6" /> is not enough, because
///     it is true on a host whose <c>::1</c> cannot be bound.
/// </summary>
public static class LocalAddresses
{
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
}
