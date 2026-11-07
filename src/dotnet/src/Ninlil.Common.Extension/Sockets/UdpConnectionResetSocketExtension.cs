using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Common.Extension.Sockets;

/// <summary>
///     This static class turns off the Windows behaviour that reports an ICMP "port unreachable"
///     for an earlier send as a receive error on a UDP socket. Every Ninlil UDP socket that
///     listens for clients calls it right after creating the socket, so a client that sends a
///     datagram and closes its port before the reply does not make the next receive fail, and an
///     off-path sender cannot provoke receive errors (and log lines) at will.
/// </summary>
public static class UdpConnectionResetSocketExtension
{
    /// <summary>
    ///     This constant contains the Winsock control code <c>SIO_UDP_CONNRESET</c>
    ///     (<c>_WSAIOW(IOC_VENDOR, 12)</c>, 0x9800000C). Microsoft documents it in the Winsock
    ///     reference ("WSAIoctl function" and "SIO_UDP_CONNRESET control code"): with the value
    ///     true (the default on Windows), an ICMP port-unreachable for a datagram this socket
    ///     sent fails a later receive with WSAECONNRESET (10054); with the value false the ICMP
    ///     message is ignored.
    /// </summary>
    private const int SioUdpConnReset = unchecked((int)0x9800000C);

    /// <summary>
    ///     This extension method makes a UDP socket ignore ICMP port-unreachable messages on
    ///     Windows. It does nothing on other systems, which have no such behaviour for an
    ///     unconnected socket. Call it on a socket that receives from many peers, never on one
    ///     that talks to a single upstream and wants a fast failure when that upstream is down.
    /// </summary>
    /// <param name="socket">The UDP socket, created and not yet closed.</param>
    public static void IgnoreIcmpPortUnreachable(this Socket socket)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        catch (SocketException)
        {
            // The option only quiets the log; a socket that refuses it still serves, and the
            // listeners' receive loops already survive the error it would have prevented.
        }
    }
}
