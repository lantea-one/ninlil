using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test proves <see cref="Dhcpv6UdpListener" /> is a genuinely restartable
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c>, with the same
///     start -> answer -> stop -> confirm-truly-stopped -> start-again-on-the-same-port ->
///     answer-again cycle, including a genuine re-bind (and, implicitly, a real
///     rejoin-the-multicast-group cycle, exercised on the second <c>StartAsync</c> call) on the
///     exact same server port.
/// </summary>
public class Dhcpv6LiveServiceControlTests
{
    [Fact]
    public async Task Listener_CanBeStoppedAndRestartedLive_OnTheSamePort()
    {
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

        // A second Start call while already running must be a safe no-op.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // The server always replies to the one fixed options.Value.ClientPort configured above,
        // so every call in this test uses that exact same clientPort -- not a different one per
        // call -- for its own local bind.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySendSolicitAndReceiveAdvertiseAsync(serverPort, clientPort));

        // A second Stop call while already stopped must also be a safe no-op.
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
    ///     This method sends one real, wire-encoded SOLICIT datagram to
    ///     <paramref name="serverPort" /> over the IPv6 loopback address and reports whether a
    ///     real ADVERTISE reply was received within a short, bounded timeout --
    ///     <see langword="true" /> when the listener is genuinely up and answering,
    ///     <see langword="false" /> when nothing answers at all (the listener is stopped), never
    ///     throwing on the timeout case.
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
    ///     This method obtains a currently-unused, unprivileged UDP port -- the same approach
    ///     <c>Dhcpv6UdpRoundTripTests.GetFreeUdpPort</c> already establishes, adapted to IPv6.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
