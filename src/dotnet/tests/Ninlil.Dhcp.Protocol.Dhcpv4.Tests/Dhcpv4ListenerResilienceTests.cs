using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks that one bad datagram or one misbehaving handler call never stops
///     the <see cref="Dhcpv4UdpListener" /> receive loop. Each test runs a real listener on a
///     free loopback port, makes the first request fail in a different way (a reply the writer
///     rejects, a handler exception, a handler timeout, an unparseable datagram), then sends a
///     second request and expects its reply while <c>IsRunning</c> is still true.
///     <para>
///         The receive loop handles datagrams one at a time, so a reply to the second request
///         proves the loop survived the first. The tests wait on a signal from the handler, not
///         on a timer.
///     </para>
/// </summary>
public class Dhcpv4ListenerResilienceTests
{
    [Fact]
    public async Task Request_WhoseReplyHasAnOptionOver255Bytes_IsDroppedAndTheNextRequestIsAnswered()
    {
        StubDhcpv4RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: request =>
            {
                Dhcpv4Message reply = new()
                {
                    OperationCode = Dhcpv4OperationCodeEnum.BootReply,
                    TransactionId = request.TransactionId,
                    ClientHardwareAddress = request.ClientHardwareAddress,
                    HardwareAddressLength = request.HardwareAddressLength,
                    ClientIpAddress = request.ClientIpAddress,
                };
                reply.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
                reply.Options.Add(new Dhcpv4Option(224, new byte[300]));
                return reply;
            }
        );
    }

    [Fact]
    public async Task Handler_ThatThrows_DoesNotStopTheListener()
    {
        StubDhcpv4RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: _ => throw new InvalidOperationException("The handler failed.")
        );
    }

    [Fact]
    public async Task Handler_ThatTimesOutWithoutAStop_DoesNotStopTheListener()
    {
        StubDhcpv4RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: _ => throw new OperationCanceledException()
        );
    }

    [Fact]
    public async Task Datagram_ThatIsNotDhcp_DoesNotStopTheListener()
    {
        StubDhcpv4RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(handler, firstFails: null);
    }

    /// <summary>
    ///     This method starts a listener, sends a first request that fails as
    ///     <paramref name="firstFails" /> says (or, when it is <see langword="null" />, a
    ///     three-byte garbage datagram instead), then sends a valid DISCOVER and expects its OFFER.
    ///     The listener must still report <c>IsRunning</c> afterwards and must stop cleanly.
    /// </summary>
    private static async Task AssertNextRequestIsAnsweredAsync(
        StubDhcpv4RequestHandler handler,
        Func<Dhcpv4Message, Dhcpv4Message> firstFails
    )
    {
        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();

        // The signal fires once the handler has seen the failing request (or, for a garbage
        // datagram, right away), so the next send is known to queue behind it.
        TaskCompletionSource firstHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        if (firstFails is not null)
        {
            handler.Override = request =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                    return NormalOffer(request);

                try
                {
                    return firstFails(request);
                }
                finally
                {
                    firstHandled.TrySetResult();
                }
            };
        }
        else
        {
            firstHandled.SetResult();
        }

        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(
                new Dhcpv4ProtocolOptions
                {
                    Enable = true,
                    ServerPort = serverPort,
                    ClientPort = clientPort,
                }
            ),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, clientPort));
            IPEndPoint server = new(IPAddress.Loopback, serverPort);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

            if (firstFails is not null)
            {
                byte[] first = Dhcpv4MessageWriter.Write(Discover(transactionId: 1));
                await client.SendAsync(first, first.Length, server);
            }
            else
            {
                await client.SendAsync([1, 2, 3], 3, server);
            }

            await firstHandled.Task.WaitAsync(timeout.Token);

            byte[] second = Dhcpv4MessageWriter.Write(Discover(transactionId: 2));
            await client.SendAsync(second, second.Length, server);

            UdpReceiveResult received = await client.ReceiveAsync(timeout.Token);

            Assert.True(
                Dhcpv4MessageReader.TryRead(
                    received.Buffer,
                    out Dhcpv4Message offer,
                    out string failureReason
                ),
                failureReason
            );
            Assert.Equal(2u, offer.TransactionId);
            Assert.True(((IDhcpProtocolService)listener).IsRunning);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.False(((IDhcpProtocolService)listener).IsRunning);
    }

    /// <summary>
    ///     This method builds a DISCOVER whose "ciaddr" is the loopback address, which makes the
    ///     listener unicast the reply to the test client (see <c>Dhcpv4UdpRoundTripTests</c>).
    /// </summary>
    private static Dhcpv4Message Discover(uint transactionId)
    {
        Dhcpv4Message discover = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x09],
            ClientIpAddress = IPAddress.Loopback,
        };
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        return discover;
    }

    /// <summary>
    ///     This method builds the plain OFFER the handler returns for every request after the
    ///     failing one.
    /// </summary>
    private static Dhcpv4Message NormalOffer(Dhcpv4Message request)
    {
        Dhcpv4Message offer = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootReply,
            TransactionId = request.TransactionId,
            YourIpAddress = StubDhcpv4RequestHandler.OfferedAddress,
            ClientIpAddress = request.ClientIpAddress,
            ClientHardwareAddress = request.ClientHardwareAddress,
            HardwareAddressLength = request.HardwareAddressLength,
        };
        offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
        return offer;
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
