using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
///     This class tests how <see cref="UdpDnsListener" /> hands datagrams to the pipeline, over a
///     real loopback socket with a fake resolver behind a real
///     <see cref="DnsQueryProcessingPipeline" />. The pipeline keeps a reference to each query's
///     bytes for the life of the query, so every datagram must reach it in an array of its own;
///     one test holds a first query inside the resolver while a second arrives and checks the
///     first one's bytes did not change. The other tests check that many clients get exactly
///     one correct answer each and that stopping the listener ends its receive loops.
/// </summary>
public sealed class UdpReceiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TwoDatagramsBackToBack_KeepTheirOwnBytesWhileTheFirstIsStillProcessed()
    {
        await using Host host = await Host.StartAsync(holdFirstQuery: true);

        using UdpClient first = new(AddressFamily.InterNetwork);
        first.Connect(IPAddress.Loopback, host.Port);
        using UdpClient second = new(AddressFamily.InterNetwork);
        second.Connect(IPAddress.Loopback, host.Port);

        // Different lengths and contents, so a shared receive buffer would show in the first
        // query's bytes as soon as the second datagram overwrites it.
        byte[] firstDatagram = DnsMessageWriter.Write(BuildQuery(1, "first-datagram.example."));
        byte[] secondDatagram = DnsMessageWriter.Write(BuildQuery(2, "b.example."));
        Assert.NotEqual(firstDatagram.Length, secondDatagram.Length);

        await first.SendAsync(firstDatagram);
        await host.Resolver.FirstQueryHeld.Task.WaitAsync(Timeout);

        await second.SendAsync(secondDatagram);
        await host.Resolver.SecondQuerySeen.Task.WaitAsync(Timeout);

        // The first query is still inside the resolver; its bytes are what it arrived with.
        byte[] firstSeen = host.Resolver.RawMessages[0];
        byte[] secondSeen = host.Resolver.RawMessages[1];
        Assert.NotSame(firstSeen, secondSeen);
        Assert.Equal(firstDatagram, firstSeen);
        Assert.Equal(secondDatagram, secondSeen);

        host.Resolver.ReleaseFirstQuery();

        Assert.Equal(1, await ReceiveIdAsync(first));
        Assert.Equal(2, await ReceiveIdAsync(second));
    }

    [Fact]
    public async Task TwoHundredQueriesFromTwentyClients_AreEachAnsweredOnceWithTheirOwnId()
    {
        await using Host host = await Host.StartAsync(holdFirstQuery: false);

        const int clients = 20;
        const int queriesEach = 10;

        Task<List<ushort>>[] runs = Enumerable
            .Range(0, clients)
            .Select(client => RunClientAsync(host.Port, client, queriesEach))
            .ToArray();
        List<ushort>[] received = await Task.WhenAll(runs);

        for (int client = 0; client < clients; client++)
        {
            // Each client sent IDs client * 100 + 0..9 one at a time and must get exactly those
            // back, in order, with no reply that belongs to another client.
            Assert.Equal(
                Enumerable.Range(0, queriesEach).Select(i => (ushort)(client * 100 + i)),
                received[client]
            );
        }

        Assert.Equal(clients * queriesEach, host.Resolver.QueryCount);
    }

    [Fact]
    public async Task StopAsync_EndsTheReceiveLoopsAndLeavesNothingListening()
    {
        Host host = await Host.StartAsync(holdFirstQuery: false);
        int port = host.Port;

        await host.DisposeAsync();

        // A datagram sent after the stop gets no answer: the socket is closed, so the send may
        // fail or be dropped, and no reply arrives either way.
        using UdpClient client = new(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, port);

        try
        {
            await client.SendAsync(DnsMessageWriter.Write(BuildQuery(7, "late.example.")));
            using CancellationTokenSource window = new(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await client.ReceiveAsync(window.Token)
            );
        }
        catch (SocketException)
        {
            // ICMP port unreachable surfaced on the connected socket: also "nothing listening".
        }
    }

    private static async Task<List<ushort>> RunClientAsync(int port, int client, int count)
    {
        using UdpClient udp = new(AddressFamily.InterNetwork);
        udp.Connect(IPAddress.Loopback, port);
        List<ushort> ids = [];

        for (int index = 0; index < count; index++)
        {
            ushort id = (ushort)(client * 100 + index);
            await udp.SendAsync(DnsMessageWriter.Write(BuildQuery(id, "many.example.")));
            ids.Add(await ReceiveIdAsync(udp));
        }

        return ids;
    }

    private static async Task<ushort> ReceiveIdAsync(UdpClient client)
    {
        using CancellationTokenSource timeout = new(Timeout);
        UdpReceiveResult reply = await client.ReceiveAsync(timeout.Token);
        return BinaryPrimitives.ReadUInt16BigEndian(reply.Buffer);
    }

    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This class owns one test's pipeline and UDP listener on the IPv4 loopback address,
    ///     started on a free port and stopped on dispose.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private UdpDnsListener _udp;

        public int Port { get; private init; }

        public CapturingResolver Resolver { get; private init; }

        public static async Task<Host> StartAsync(bool holdFirstQuery)
        {
            Host host = new()
            {
                Port = LocalAddresses.GetFreePort(),
                Resolver = new CapturingResolver(holdFirstQuery),
            };

            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([host.Resolver], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            host._udp = new UdpDnsListener(
                host._pipeline,
                new StaticOptionsMonitor<PlainProtocolOptions>(
                    new PlainProtocolOptions { Port = host.Port, ListenAddresses = "127.0.0.1" }
                ),
                NullLogger<UdpDnsListener>.Instance
            );
            await host._udp.StartAsync(CancellationToken.None);

            // BackgroundService runs ExecuteAsync (which binds) as a continuation, so wait for
            // the listener to report that it is receiving.
            for (int attempt = 0; attempt < 500 && !host._udp.IsListening; attempt++)
                await Task.Delay(10);

            Assert.True(host._udp.IsListening);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            // StopAsync returns only when ExecuteAsync, and so every receive loop, has ended.
            await _udp.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    /// <summary>
    ///     This resolver records the array each query's raw message is (the array the listener
    ///     handed over) and a copy of its bytes at the time the resolver saw it. When asked to,
    ///     it holds the first query until the test releases it, so a second datagram can arrive
    ///     while the first is still being processed. It answers with an empty NOERROR response.
    /// </summary>
    private sealed class CapturingResolver(bool holdFirstQuery) : IDnsQueryResolver
    {
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly ConcurrentQueue<byte[]> _rawMessages = new();
        private int _queryCount;

        /// <summary>
        ///     This property completes when the first query has reached the resolver and is held.
        /// </summary>
        public TaskCompletionSource FirstQueryHeld { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     This property completes when the second query has reached the resolver.
        /// </summary>
        public TaskCompletionSource SecondQuerySeen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     This property lists the raw message of each query, in arrival order.
        /// </summary>
        public List<byte[]> RawMessages => [.. _rawMessages];

        /// <summary>
        ///     This property contains the number of queries the resolver has seen.
        /// </summary>
        public int QueryCount => Volatile.Read(ref _queryCount);

        /// <summary>
        ///     This method lets the held first query finish.
        /// </summary>
        public void ReleaseFirstQuery() => _releaseFirst.TrySetResult();

        /// <inheritdoc />
        public async ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            _rawMessages.Enqueue(context.RawMessage);
            int number = Interlocked.Increment(ref _queryCount);

            if (number == 2)
                SecondQuerySeen.TrySetResult();

            if (holdFirstQuery && number == 1)
            {
                FirstQueryHeld.TrySetResult();
                await _releaseFirst.Task.WaitAsync(stoppingToken);
            }

            return DnsQueryResolution.Handled(
                new DnsMessage
                {
                    Id = context.Request.Id,
                    IsResponse = true,
                    Opcode = context.Request.Opcode,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    Questions = { context.Request.Questions[0] },
                }
            );
        }
    }

    /// <summary>This publisher discards every event.</summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
