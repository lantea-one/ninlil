using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test class checks that one bad datagram or one misbehaving handler call never stops
///     the <see cref="Dhcpv6UdpListener" /> receive loop, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4ListenerResilienceTests</c>. Each test runs a
///     real listener on a free IPv6 loopback port, makes the first request fail in a different
///     way (a reply the writer rejects, a handler exception, a handler timeout, an unparseable
///     datagram), then sends a second request and expects its reply while <c>IsRunning</c> is
///     still true.
///     <para>
///         The receive loop handles datagrams one at a time, so a reply to the second request
///         proves the loop survived the first. The tests wait on a signal from the handler, not
///         on a timer.
///     </para>
/// </summary>
public class Dhcpv6ListenerResilienceTests
{
    [Fact]
    public async Task Request_WhoseReplyHasAnOptionOver65535Bytes_IsDroppedAndTheNextRequestIsAnswered()
    {
        StubDhcpv6RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: request =>
            {
                Dhcpv6Message reply = new()
                {
                    MessageType = Dhcpv6MessageTypeEnum.Advertise,
                    TransactionId = request.TransactionId,
                };
                reply.Options.Add(new Dhcpv6Option(60000, new byte[70000]));
                return reply;
            }
        );
    }

    [Fact]
    public async Task Handler_ThatThrows_DoesNotStopTheListener()
    {
        StubDhcpv6RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: _ => throw new InvalidOperationException("The handler failed.")
        );
    }

    [Fact]
    public async Task Handler_ThatTimesOutWithoutAStop_DoesNotStopTheListener()
    {
        StubDhcpv6RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(
            handler,
            firstFails: _ => throw new OperationCanceledException()
        );
    }

    [Fact]
    public async Task Datagram_ThatIsNotDhcp_DoesNotStopTheListener()
    {
        StubDhcpv6RequestHandler handler = new();

        await AssertNextRequestIsAnsweredAsync(handler, firstFails: null);
    }

    /// <summary>
    ///     This method starts a listener, sends a first request that fails as
    ///     <paramref name="firstFails" /> says (or, when it is <see langword="null" />, a
    ///     one-byte garbage datagram instead), then sends a valid SOLICIT and expects its
    ///     ADVERTISE. The listener must still report <c>IsRunning</c> afterwards and must stop
    ///     cleanly.
    /// </summary>
    private static async Task AssertNextRequestIsAnsweredAsync(
        StubDhcpv6RequestHandler handler,
        Func<Dhcpv6Message, Dhcpv6Message> firstFails
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
                    return NormalAdvertise(request);

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

        Dhcpv6UdpListener listener = new(
            handler,
            Options.Create(
                new Dhcpv6ProtocolOptions
                {
                    Enable = true,
                    ServerPort = serverPort,
                    ClientPort = clientPort,
                }
            ),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            using UdpClient client = new(AddressFamily.InterNetworkV6);
            client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, clientPort));
            IPEndPoint server = new(IPAddress.IPv6Loopback, serverPort);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

            if (firstFails is not null)
            {
                byte[] first = Dhcpv6MessageWriter.Write(Solicit(transactionId: 1));
                await client.SendAsync(first, first.Length, server);
            }
            else
            {
                await client.SendAsync([0xFF], 1, server);
            }

            await firstHandled.Task.WaitAsync(timeout.Token);

            byte[] second = Dhcpv6MessageWriter.Write(Solicit(transactionId: 2));
            await client.SendAsync(second, second.Length, server);

            UdpReceiveResult received = await client.ReceiveAsync(timeout.Token);

            Assert.True(
                Dhcpv6MessageReader.TryRead(
                    received.Buffer,
                    out Dhcpv6Message advertise,
                    out string failureReason
                ),
                failureReason
            );
            Assert.Equal(2u, advertise.TransactionId);
            Assert.True(((IDhcpProtocolService)listener).IsRunning);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.False(((IDhcpProtocolService)listener).IsRunning);
    }

    /// <summary>
    ///     This method builds a minimal SOLICIT with the given transaction ID.
    /// </summary>
    private static Dhcpv6Message Solicit(uint transactionId)
    {
        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = transactionId,
        };
        solicit.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66]);
        solicit.SetElapsedTime(TimeSpan.Zero);
        return solicit;
    }

    /// <summary>
    ///     This method builds the plain ADVERTISE the handler returns for every request after the
    ///     failing one.
    /// </summary>
    private static Dhcpv6Message NormalAdvertise(Dhcpv6Message request)
    {
        Dhcpv6Message advertise = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Advertise,
            TransactionId = request.TransactionId,
        };
        advertise.SetServerIdentifier(StubDhcpv6RequestHandler.ServerIdentifier);
        return advertise;
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
