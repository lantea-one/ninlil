using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class closes a TCP connection after the server has written its last reply, without
///     destroying that reply. Closing a socket that still holds unread data makes the operating
///     system send a reset (RST) instead of a normal close. On Windows the reset makes the peer
///     discard data it has received and not yet read, so the final reply is lost. A listener
///     calls <see cref="CloseAsync" /> when <see cref="FramedDnsStreamSession.ClosedAfterDrain" />
///     is set: a zone transfer or a DNSCrypt transaction is over, but the client may already have
///     sent another message that the session never read.
/// </summary>
public static class LingeringSocketClose
{
    /// <summary>
    ///     This constant contains how long <see cref="CloseAsync" /> waits for the peer to close
    ///     its side. The wait holds the connection's limiter slot, so it stays short.
    /// </summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     This method sends the closing FIN, discards what the peer still sends until the peer
    ///     closes or <paramref name="maxWait" /> passes, and then closes the socket. Reading the
    ///     data empties the receive queue, so the final close is not a reset. It never throws.
    /// </summary>
    /// <param name="socket">The connection to close. It is closed before the method returns.</param>
    /// <param name="maxWait">The longest to wait for the peer; <see cref="DefaultMaxWait" />.</param>
    public static async Task CloseAsync(Socket socket, TimeSpan maxWait)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Send);

            using CancellationTokenSource timeout = new(maxWait);
            byte[] discard = new byte[512];

            // A read of zero octets is the peer's own close.
            while (await socket.ReceiveAsync(discard, SocketFlags.None, timeout.Token) > 0) { }
        }
        catch (Exception exception)
            when (exception
                    is SocketException
                        or ObjectDisposedException
                        or OperationCanceledException
            )
        {
            // The peer reset the connection, the wait ran out or the socket is gone. Nothing
            // is left to protect.
        }
        finally
        {
            socket.Close();
        }
    }
}
