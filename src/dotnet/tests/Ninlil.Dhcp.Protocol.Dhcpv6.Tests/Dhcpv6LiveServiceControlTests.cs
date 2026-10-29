using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test class checks that <see cref="Dhcpv6UdpListener" /> can be started and stopped
///     repeatedly, like <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c>:
///     start, answer, stop, confirm silence, start again on the same port (re-binding and
///     re-joining the multicast group), answer again. It uses IPv6 loopback sockets, high ports
///     and <see cref="StubDhcpv6RequestHandler" />.
/// </summary>
public class Dhcpv6LiveServiceControlTests
{
    [Fact]
    public async Task Listener_CanBeStoppedAndRestartedLive_OnTheSamePort()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();

        StubDhcpv6RequestHandler handler = new();
        Dhcpv6ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = serverPort,
            ClientPort = clientPort,
        };
        Dhcpv6UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);
        Assert.True(await TrySendSolicitAndReceiveAdvertiseAsync(serverPort, clientPort));

        // Starting again while running must do nothing.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // The server always replies to the configured ClientPort, so every call binds that same
        // client port.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySendSolicitAndReceiveAdvertiseAsync(serverPort, clientPort));

        // Stopping again while stopped must also do nothing.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        await listener.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilListeningAsync(listener);
            Assert.True(await TrySendSolicitAndReceiveAdvertiseAsync(serverPort, clientPort));
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilListeningAsync(Dhcpv6UdpListener listener)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!listener.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(listener.IsListening, "The listener never finished binding its socket.");
    }

    /// <summary>
    ///     This method sends one encoded SOLICIT to <paramref name="serverPort" /> over IPv6
    ///     loopback and reports whether an ADVERTISE arrived within a short timeout. A timeout
    ///     returns <see langword="false" /> rather than throwing.
    /// </summary>
    private static async Task<bool> TrySendSolicitAndReceiveAdvertiseAsync(
        int serverPort,
        int clientPort
    )
    {
        using UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, clientPort));

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC];

        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x5A5A5A,
        };
        solicit.SetClientIdentifier(clientDuid);
        solicit.SetElapsedTime(TimeSpan.Zero);

        byte[] solicitBytes = Dhcpv6MessageWriter.Write(solicit);
        await client.SendAsync(
            solicitBytes,
            solicitBytes.Length,
            new IPEndPoint(IPAddress.IPv6Loopback, serverPort)
        );

        using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(1));

        try
        {
            UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);
            return Dhcpv6MessageReader.TryRead(received.Buffer, out _, out _);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method gets an unused high IPv6 UDP port, as
    ///     <c>Dhcpv6UdpRoundTripTests.GetFreeUdpPort</c> does.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
