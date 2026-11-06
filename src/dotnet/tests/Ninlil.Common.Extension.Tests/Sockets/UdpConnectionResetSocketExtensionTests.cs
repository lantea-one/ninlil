using System.Net;
using System.Net.Sockets;
using Ninlil.Common.Extension.Sockets;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Sockets;

/// <summary>
///     These tests cover <see cref="UdpConnectionResetSocketExtension" /> over real UDP sockets on
///     the IPv4 loopback with OS-assigned ports. On Windows they send to a port nobody listens on
///     (which makes the stack report an ICMP port-unreachable) and check that the next receive
///     still returns a datagram from another sender; a control socket without the call shows the
///     failure the extension prevents. On other systems the call must be a harmless no-op.
/// </summary>
public class UdpConnectionResetSocketExtensionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task IgnoreIcmpPortUnreachable_AfterASendToAClosedPort_StillReceivesFromAnotherSender()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using Socket listener = CreateBoundSocket();
        listener.IgnoreIcmpPortUnreachable();

        await ProvokeIcmpPortUnreachableAsync(listener);

        using Socket sender = CreateBoundSocket();
        sender.SendTo([1, 2, 3], listener.LocalEndPoint);

        Assert.Equal(3, await ReceiveLengthAsync(listener));
    }

    [Fact]
    public async Task WithoutTheCall_AfterASendToAClosedPort_TheNextReceiveFailsWithConnectionReset()
    {
        // The control for the test above: the platform really does report the error, so the
        // extension's test cannot pass by accident. Windows only.
        if (!OperatingSystem.IsWindows())
            return;

        using Socket listener = CreateBoundSocket();

        await ProvokeIcmpPortUnreachableAsync(listener);

        using Socket sender = CreateBoundSocket();
        sender.SendTo([1, 2, 3], listener.LocalEndPoint);

        SocketException failure = await Assert.ThrowsAsync<SocketException>(() =>
            ReceiveLengthAsync(listener)
        );
        Assert.Equal(SocketError.ConnectionReset, failure.SocketErrorCode);
    }

    [Fact]
    public async Task IgnoreIcmpPortUnreachable_OnAnySystem_LeavesTheSocketReceiving()
    {
        // Linux and macOS have nothing to turn off; the call must not throw or change a thing.
        using Socket listener = CreateBoundSocket();
        listener.IgnoreIcmpPortUnreachable();

        using Socket sender = CreateBoundSocket();
        sender.SendTo([9, 9], listener.LocalEndPoint);

        Assert.Equal(2, await ReceiveLengthAsync(listener));
    }

    private static Socket CreateBoundSocket()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    /// <summary>
    ///     This method makes <paramref name="listener" /> send to a loopback port that nothing
    ///     listens on (a socket bound and closed right here, so the OS just handed it out) and
    ///     waits for the ICMP message to arrive.
    /// </summary>
    private static async Task ProvokeIcmpPortUnreachableAsync(Socket listener)
    {
        IPEndPoint closed;

        using (Socket gone = CreateBoundSocket())
            closed = (IPEndPoint)gone.LocalEndPoint;

        listener.SendTo([0], closed);
        await Task.Delay(300);
    }

    private static async Task<int> ReceiveLengthAsync(Socket socket)
    {
        using CancellationTokenSource timeout = new(Timeout);
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            new byte[64],
            new IPEndPoint(IPAddress.Any, 0),
            timeout.Token
        );
        return result.ReceivedBytes;
    }
}
