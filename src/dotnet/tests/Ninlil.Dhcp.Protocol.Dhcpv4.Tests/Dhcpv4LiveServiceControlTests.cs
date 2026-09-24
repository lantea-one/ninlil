using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test proves <see cref="Dhcpv4UdpListener" /> is a genuinely restartable
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c>, not merely a single-shot
///     <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> whose
///     <c>StartAsync</c>/<c>StopAsync</c> only ever run once each for the whole host lifetime
///     (see this class's own doc comment for the design). It exercises a real
///     start -> answer -> stop -> confirm-truly-stopped -> start-again-on-the-same-port ->
///     answer-again cycle against a real bound socket, which is exactly what live DHCP
///     service control (a future REST surface's own job) depends on actually working.
/// </summary>
public class Dhcpv4LiveServiceControlTests
{
    [Fact]
    public async Task Listener_CanBeStoppedAndRestartedLive_OnTheSamePort()
    {
        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();

        StubDhcpv4RequestHandler handler = new();
        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = serverPort,
            ClientPort = clientPort,
        };
        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        // First cycle: start, confirm a real request is answered.
        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);
        Assert.True(await TrySendDiscoverAndReceiveOfferAsync(serverPort, clientPort));

        // A second, concurrent-looking Start call while already running must be a safe no-op,
        // not a crash or a second bind attempt on the same port.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // Stop: the socket must genuinely be released -- a request sent while stopped must
        // receive no reply at all, not merely a slower one. The server always replies to the
        // one fixed options.Value.ClientPort configured above, so every call in this test uses
        // that exact same clientPort -- not a different one per call -- for its own local bind.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySendDiscoverAndReceiveOfferAsync(serverPort, clientPort));

        // A second, concurrent-looking Stop call while already stopped must also be a safe
        // no-op.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        // Second cycle: start again, on the exact same server port -- this only succeeds if
        // StopAsync genuinely released the socket rather than merely stopping the receive loop.
        await listener.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilListeningAsync(listener);
            Assert.True(await TrySendDiscoverAndReceiveOfferAsync(serverPort, clientPort));
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This method waits, polling, for <paramref name="listener" /> to report itself bound,
    ///     since <see cref="Dhcpv4UdpListener.StartAsync" /> returns as soon as the bind itself
    ///     completes, but the caller here still wants a deterministic point to send from.
    /// </summary>
    private static async Task WaitUntilListeningAsync(Dhcpv4UdpListener listener)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!listener.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(listener.IsListening, "The listener never finished binding its socket.");
    }

    /// <summary>
    ///     This method sends one real, wire-encoded DISCOVER datagram to
    ///     <paramref name="serverPort" /> and reports whether a real OFFER reply was received
    ///     within a short, bounded timeout -- <see langword="true" /> when the listener is
    ///     genuinely up and answering, <see langword="false" /> when nothing answers at all
    ///     (the listener is stopped), never throwing on the timeout case.
    /// </summary>
    private static async Task<bool> TrySendDiscoverAndReceiveOfferAsync(
        int serverPort,
        int clientPort
    )
    {
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, clientPort));

        Dhcpv4Message discover = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x5A5A5A5A,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x03],
            ClientIpAddress = IPAddress.Loopback,
        };
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        byte[] discoverBytes = Dhcpv4MessageWriter.Write(discover);
        await client.SendAsync(
            discoverBytes,
            discoverBytes.Length,
            new IPEndPoint(IPAddress.Loopback, serverPort)
        );

        using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(1));

        try
        {
            UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);
            return Dhcpv4MessageReader.TryRead(received.Buffer, out _, out _);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged UDP port -- the same approach
    ///     <c>Dhcpv4UdpRoundTripTests.GetFreeUdpPort</c> already establishes.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }
}
