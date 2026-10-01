using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks that <see cref="Dhcpv4UdpListener" /> can be started and stopped
///     repeatedly as a <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c>, which runtime
///     service control (<c>DhcpServiceControlController</c>) relies on. On loopback UDP sockets
///     with high ports and <see cref="StubDhcpv4RequestHandler" />, it runs start, answer, stop,
///     confirm silence, start again on the same port, answer again, and checks that repeated
///     start and stop calls do nothing.
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

        // First cycle: start and confirm a request is answered.
        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);
        Assert.True(await TrySendDiscoverAndReceiveOfferAsync(serverPort, clientPort));

        // Starting again while running must do nothing, not try a second bind on the port.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // After a stop, a request must get no reply at all. The server always replies to the
        // configured ClientPort, so every call binds that same client port.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySendDiscoverAndReceiveOfferAsync(serverPort, clientPort));

        // Stopping again while stopped must also do nothing.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        // Second cycle on the same server port, which only works if StopAsync released the
        // socket rather than just stopping the receive loop.
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
    ///     This method polls until <paramref name="listener" /> reports that it is listening.
    ///     <see cref="Dhcpv4UdpListener.StartAsync" /> already binds before returning; the poll
    ///     only guards against that changing.
    /// </summary>
    private static async Task WaitUntilListeningAsync(Dhcpv4UdpListener listener)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!listener.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(listener.IsListening, "The listener never finished binding its socket.");
    }

    /// <summary>
    ///     This method sends one encoded DISCOVER to <paramref name="serverPort" /> and reports
    ///     whether an OFFER arrived within a short timeout. A timeout returns
    ///     <see langword="false" /> rather than throwing.
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
    ///     This method gets an unused high UDP port, as
    ///     <c>Dhcpv4UdpRoundTripTests.GetFreeUdpPort</c> does.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }
}
