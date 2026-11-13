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
        // DHCP needs Linux (CHANGELOG.md: "DHCP, Router Advertisement and passive device presence
        // need Linux"). On Windows a UDP receive also surfaces an ICMP port-unreachable as a
        // connection reset, so this test cannot run there.
        if (!OperatingSystem.IsLinux())
            return;

        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        // The client socket binds first, on an OS-assigned port, because the listener sends its
        // replies to the configured client port. One client socket serves every cycle.
        using UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        int clientPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;

        StubDhcpv6RequestHandler handler = new();
        Dhcpv6ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = 0,
            ClientPort = clientPort,
        };
        Dhcpv6UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);
        int serverPort = listener.BoundPort;
        Assert.True(await TrySendSolicitAndReceiveAdvertiseAsync(client, serverPort));

        // The second cycle must rebind this same port, so the options now name it instead of 0.
        // The listener reads the option on every start.
        options.ServerPort = serverPort;

        // Starting again while running must do nothing.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // The server always replies to the configured ClientPort, so every call uses the one
        // client socket bound to it.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySendSolicitAndReceiveAdvertiseAsync(client, serverPort));

        // Stopping again while stopped must also do nothing.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        // Second cycle on the same server port, which only works if StopAsync released the
        // socket. Port 0 cannot serve here, because the test needs the same number again.
        // Another process could in theory take the number between the stop and this start, so
        // an AddressInUse is retried a bounded number of times. A socket StopAsync failed to
        // release keeps failing, so the retry cannot hide it.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await listener.StartAsync(CancellationToken.None);
                break;
            }
            catch (SocketException exception)
                when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 3)
            {
                await Task.Delay(50);
            }
        }

        try
        {
            await WaitUntilListeningAsync(listener);
            Assert.True(await TrySendSolicitAndReceiveAdvertiseAsync(client, serverPort));
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
    ///     This method sends one encoded SOLICIT from <paramref name="client" /> to
    ///     <paramref name="serverPort" /> over IPv6 loopback and reports whether an ADVERTISE arrived within a short timeout. A timeout
    ///     returns <see langword="false" /> rather than throwing.
    /// </summary>
    private static async Task<bool> TrySendSolicitAndReceiveAdvertiseAsync(
        UdpClient client,
        int serverPort
    )
    {
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
}
