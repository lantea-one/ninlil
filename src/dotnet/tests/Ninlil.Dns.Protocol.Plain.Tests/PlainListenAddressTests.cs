using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class runs <see cref="UdpDnsListener" /> and <see cref="TcpDnsListener" /> on real
///     sockets with different <see cref="PlainProtocolOptions.ListenAddresses" />, sends queries
///     to local addresses and checks which get answered. A fake resolver answers every query.
///     Each test starts its listeners on an OS-assigned port, since the addresses differ per
///     test.
/// </summary>
public sealed class PlainListenAddressTests
{
    private static readonly IPAddress ExpectedAnswer = IPAddress.Parse("192.0.2.53");
    private static readonly TimeSpan NoAnswerWindow = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task NoListenAddresses_AnswersOnEveryAddressAsBefore()
    {
        await using RunningListeners listeners = await RunningListeners.StartAsync("");

        Assert.NotNull(await QueryUdpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(IPAddress.Loopback, listeners.Port));

        if (LocalAddresses.CanBind(IPAddress.IPv6Loopback))
        {
            Assert.NotNull(await QueryUdpAsync(IPAddress.IPv6Loopback, listeners.Port));
            Assert.NotNull(await QueryTcpAsync(IPAddress.IPv6Loopback, listeners.Port));
        }

        Assert.Empty(listeners.Udp.FailedListenAddresses);
        Assert.Empty(listeners.Tcp.FailedListenAddresses);
    }

    [Fact]
    public async Task NoListenAddresses_WhenThePortIsTaken_LogsAnErrorReportsNotListeningAndKeepsRunning()
    {
        // Another program holds the wildcard UDP and TCP port. Neither listener may fault: a
        // faulted hosted service stops the whole host.
        using Socket tcpOccupant = new(
            AddressFamily.InterNetworkV6,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        tcpOccupant.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        tcpOccupant.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        tcpOccupant.Listen();
        int port = ((IPEndPoint)tcpOccupant.LocalEndPoint).Port;

        using Socket udpOccupant = new(
            AddressFamily.InterNetworkV6,
            SocketType.Dgram,
            ProtocolType.Udp
        );
        udpOccupant.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        udpOccupant.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

        await using RunningListeners listeners = await RunningListeners.StartAsync("", port);

        Assert.False(listeners.Udp.ExecuteTask.IsFaulted);
        Assert.False(listeners.Tcp.ExecuteTask.IsFaulted);
        Assert.False(listeners.Udp.IsListening);
        Assert.False(listeners.Tcp.IsListening);
        Assert.Single(listeners.Udp.FailedListenAddresses);
        Assert.Single(listeners.Tcp.FailedListenAddresses);
        Assert.Contains(
            listeners.UdpLogger.Errors,
            message => message.Contains("Plain DNS (UDP)") && message.Contains($"port {port}")
        );
        Assert.Contains(
            listeners.TcpLogger.Errors,
            message => message.Contains("Plain DNS (TCP)") && message.Contains($"port {port}")
        );
    }

    [Fact]
    public async Task LoopbackOnly_AnswersThereButNotOnAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        await using RunningListeners listeners = await RunningListeners.StartAsync("127.0.0.1");

        DnsMessage udpAnswer = await QueryUdpAsync(IPAddress.Loopback, listeners.Port);
        Assert.NotNull(udpAnswer);
        Assert.Equal(ExpectedAnswer.GetAddressBytes(), udpAnswer.Answers[0].Data.ToArray());
        Assert.NotNull(await QueryTcpAsync(IPAddress.Loopback, listeners.Port));

        Assert.Null(await QueryUdpAsync(other, listeners.Port));
        Assert.Null(await QueryTcpAsync(other, listeners.Port));
    }

    [Fact]
    public async Task TwoExplicitAddresses_BothAnswer()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        await using RunningListeners listeners = await RunningListeners.StartAsync(
            $"127.0.0.1, {other}"
        );

        Assert.NotNull(await QueryUdpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryUdpAsync(other, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(other, listeners.Port));
    }

    [Fact]
    public async Task BothWildcards_AnswerIPv4AndIPv6WithoutColliding()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        await using RunningListeners listeners = await RunningListeners.StartAsync("0.0.0.0, ::");

        Assert.Empty(listeners.Udp.FailedListenAddresses);
        Assert.Empty(listeners.Tcp.FailedListenAddresses);
        Assert.NotNull(await QueryUdpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryUdpAsync(IPAddress.IPv6Loopback, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(IPAddress.IPv6Loopback, listeners.Port));
    }

    [Fact]
    public async Task ABadAddress_IsLoggedAndReported_WhileTheOthersKeepAnswering()
    {
        await using RunningListeners listeners = await RunningListeners.StartAsync(
            $"127.0.0.1, {LocalAddresses.Unassigned}, eth0"
        );

        Assert.True(listeners.Udp.IsListening);
        Assert.True(listeners.Tcp.IsListening);
        Assert.NotNull(await QueryUdpAsync(IPAddress.Loopback, listeners.Port));
        Assert.NotNull(await QueryTcpAsync(IPAddress.Loopback, listeners.Port));

        Assert.Equal(2, listeners.Udp.FailedListenAddresses.Count);
        Assert.Equal(2, listeners.Tcp.FailedListenAddresses.Count);
        Assert.Contains(
            listeners.UdpLogger.Errors,
            message => message.Contains("Plain DNS (UDP)") && message.Contains("192.0.2.1")
        );
        Assert.Contains(
            listeners.TcpLogger.Errors,
            message => message.Contains("Plain DNS (TCP)") && message.Contains("192.0.2.1")
        );
        Assert.Contains(listeners.TcpLogger.Errors, message => message.Contains("'eth0'"));
    }

    [Fact]
    public async Task OnlyUnusableAddresses_StaysDownWithoutThrowing()
    {
        await using RunningListeners listeners = await RunningListeners.StartAsync(
            LocalAddresses.Unassigned.ToString()
        );

        Assert.False(listeners.Udp.IsListening);
        Assert.False(listeners.Tcp.IsListening);

        // The listener gave up quietly (logged, not thrown), so its background task finished
        // cleanly and the host it runs in is never asked to stop.
        Assert.True(listeners.Udp.ExecuteTask.IsCompletedSuccessfully);
        Assert.True(listeners.Tcp.ExecuteTask.IsCompletedSuccessfully);
        Assert.Contains(
            listeners.UdpLogger.Errors,
            message => message.Contains("not listening anywhere")
        );
    }

    /// <summary>
    ///     This method sends one query over UDP and waits briefly for the answer, returning
    ///     <see langword="null" /> when nothing answers (a timeout, or the ICMP "port unreachable"
    ///     a closed port sends back).
    /// </summary>
    private static async Task<DnsMessage> QueryUdpAsync(IPAddress address, int port)
    {
        using UdpClient client = new(address.AddressFamily);
        client.Connect(address, port);
        await client.SendAsync(DnsMessageWriter.Write(BuildQuery()));

        using CancellationTokenSource timeout = new(NoAnswerWindow);

        try
        {
            UdpReceiveResult result = await client.ReceiveAsync(timeout.Token);
            return DnsMessageReader.Read(result.Buffer, out _);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or SocketException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method sends one length-prefixed query over TCP, returning <see langword="null" />
    ///     when the connection is refused.
    /// </summary>
    private static async Task<DnsMessage> QueryTcpAsync(IPAddress address, int port)
    {
        using TcpClient client = new(address.AddressFamily);

        try
        {
            await client.ConnectAsync(address, port);
        }
        catch (SocketException)
        {
            return null;
        }

        NetworkStream stream = client.GetStream();
        byte[] message = DnsMessageWriter.Write(BuildQuery());
        byte[] prefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)message.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(message);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await stream.ReadExactlyAsync(prefix, timeout.Token);
        byte[] response = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await stream.ReadExactlyAsync(response, timeout.Token);

        return DnsMessageReader.Read(response, out _);
    }

    private static DnsMessage BuildQuery() =>
        new()
        {
            Id = 4242,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("listen.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This class owns one test's pipeline plus UDP and TCP listeners, started together on a
    ///     free port with the given listen addresses, and stops them all on dispose.
    /// </summary>
    private sealed class RunningListeners : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;

        public int Port { get; private init; }

        public UdpDnsListener Udp { get; private set; }

        public TcpDnsListener Tcp { get; private set; }

        public CapturingLogger<UdpDnsListener> UdpLogger { get; } = new();

        public CapturingLogger<TcpDnsListener> TcpLogger { get; } = new();

        public static async Task<RunningListeners> StartAsync(
            string listenAddresses,
            int? port = null
        )
        {
            RunningListeners running = new() { Port = port ?? LocalAddresses.GetFreePort() };

            running._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher(
                    [new AlwaysAnswerAResolver(ExpectedAnswer)],
                    new NoOpEventPublisher()
                ),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await running._pipeline.StartAsync(CancellationToken.None);

            StaticOptionsMonitor<PlainProtocolOptions> options = new(
                new PlainProtocolOptions { Port = running.Port, ListenAddresses = listenAddresses }
            );

            running.Udp = new UdpDnsListener(running._pipeline, options, running.UdpLogger);
            running.Tcp = new TcpDnsListener(
                running._pipeline,
                new ServiceCollection()
                    .BuildServiceProvider()
                    .GetRequiredService<IServiceScopeFactory>(),
                options,
                running.TcpLogger
            );

            await running.Udp.StartAsync(CancellationToken.None);
            await running.Tcp.StartAsync(CancellationToken.None);

            // BackgroundService runs ExecuteAsync (which binds) as a continuation, so give both
            // listeners a moment to bind before the test sends anything.
            await Task.Delay(200);
            return running;
        }

        public async ValueTask DisposeAsync()
        {
            await Tcp.StopAsync(CancellationToken.None);
            await Udp.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    /// <summary>
    ///     This resolver answers every query with one fixed A record. Resolution is not under test
    ///     here, only which addresses a query reaches.
    /// </summary>
    private sealed class AlwaysAnswerAResolver(IPAddress answer) : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };

            response.Answers.Add(
                new DnsResourceRecord(
                    context.Request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    answer.GetAddressBytes()
                )
            );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    /// <summary>This publisher discards every event.</summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
