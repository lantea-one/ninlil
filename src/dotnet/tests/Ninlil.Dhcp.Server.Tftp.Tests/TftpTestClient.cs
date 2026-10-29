using System.Net;
using System.Net.Sockets;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class holds the client side of the loopback UDP tests of <see cref="TftpServer" />:
///     building a read request, polling for a condition, and sending a request the way a real
///     TFTP client does. UDP gives no delivery guarantee, so a client resends its request until
///     it gets an answer. Tests that assume one send always arrives fail when the kernel drops a
///     datagram from a full receive buffer or the server has not freed a slot yet.
///     <see cref="TftpServerProblemSummaryTests" /> and <see cref="TftpServerTransferCapTests" />
///     use it.
/// </summary>
public static class TftpTestClient
{
    /// <summary>
    ///     This method builds a Read Request (opcode 1) for <paramref name="filename" /> in octet
    ///     mode.
    /// </summary>
    public static byte[] BuildReadRequest(string filename) =>
        [0x00, 0x01, .. Encoding.ASCII.GetBytes(filename), 0x00, .. "octet"u8.ToArray(), 0x00];

    /// <summary>
    ///     This method waits up to five seconds for a condition and returns when it holds or the
    ///     time is up, so the assertion that follows reports the failure.
    /// </summary>
    public static async Task PollAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    /// <summary>
    ///     This method sends a read request to <paramref name="server" /> every
    ///     <paramref name="attemptWindow" /> until a reply arrives and returns that reply, or
    ///     returns <see langword="null" /> after five seconds without one. A resent request that
    ///     the server already answered starts a second transfer, so a test that counts transfers
    ///     passes a window long enough for the first answer.
    /// </summary>
    /// <param name="client">The socket that sends the request and receives the reply.</param>
    /// <param name="server">The server's listening endpoint.</param>
    /// <param name="filename">The file to request.</param>
    /// <param name="attemptWindow">How long to wait for a reply before sending again; 100 ms when omitted.</param>
    public static async Task<UdpReceiveResult?> RequestUntilAnsweredAsync(
        UdpClient client,
        IPEndPoint server,
        string filename,
        TimeSpan? attemptWindow = null
    )
    {
        TimeSpan window = attemptWindow ?? TimeSpan.FromMilliseconds(100);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await client.SendAsync(BuildReadRequest(filename), server);
            // The receive is cancelled, not abandoned: an abandoned receive stays pending on the
            // socket and swallows the reply to the next attempt.
            using CancellationTokenSource timeout = new(window);
            try
            {
                return await client.ReceiveAsync(timeout.Token);
            }
            catch (OperationCanceledException) { }
        }

        return null;
    }

    /// <summary>
    ///     This method reports whether a datagram arrives at <paramref name="client" /> within
    ///     <paramref name="timeout" />. Tests use it for both sides: a long timeout for "the
    ///     server answers" and a short one for "the server stays silent".
    /// </summary>
    public static async Task<bool> ReceivesAnythingAsync(UdpClient client, TimeSpan timeout)
    {
        // The receive is cancelled when the time is up, so no pending receive is left on the
        // socket to swallow a later datagram.
        using CancellationTokenSource cancellation = new(timeout);
        try
        {
            await client.ReceiveAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method reports whether this host can bind a UDP socket to
    ///     <paramref name="address" />. The tests that use 127.0.0.2 as a second client address
    ///     return early on a host that cannot.
    /// </summary>
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
