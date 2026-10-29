using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class tests how <see cref="DnsCryptUdpListener" /> hands encrypted queries to the
///     pipeline, over a real loopback socket with its own certificate, a real
///     <see cref="DnsQueryProcessingPipeline" /> and a fake resolver. The pipeline keeps a
///     reference to each query's bytes for the life of the query, so one test holds a first
///     query inside the resolver while a second datagram arrives, checks that the first one's
///     bytes did not change, and then decrypts both replies to check each client gets its own
///     answer. It also checks that stopping the listener ends its receive loops.
/// </summary>
public sealed class DnsCryptUdpReceiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TwoDatagramsBackToBack_KeepTheirOwnBytesWhileTheFirstIsStillProcessed()
    {
        await using Host host = await Host.StartAsync();

        DnsMessage firstQuery = DnsCryptWireTestHelper.BuildQuery(
            1,
            DnsName.Parse("first-datagram.example.test.")
        );
        DnsMessage secondQuery = DnsCryptWireTestHelper.BuildQuery(
            2,
            DnsName.Parse("b.example.test.")
        );
        byte[] firstBytes = DnsMessageWriter.Write(firstQuery);
        byte[] secondBytes = DnsMessageWriter.Write(secondQuery);
        Assert.NotEqual(firstBytes.Length, secondBytes.Length);

        (byte[] firstPacket, DnsCryptWireTestHelper.ClientQuerySession firstSession) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(host.Certificate, firstQuery);
        (byte[] secondPacket, DnsCryptWireTestHelper.ClientQuerySession secondSession) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(host.Certificate, secondQuery);

        using UdpClient first = new(AddressFamily.InterNetwork);
        first.Connect(IPAddress.Loopback, host.Port);
        using UdpClient second = new(AddressFamily.InterNetwork);
        second.Connect(IPAddress.Loopback, host.Port);

        await first.SendAsync(firstPacket);
        await host.Resolver.FirstQueryHeld.Task.WaitAsync(Timeout);

        await second.SendAsync(secondPacket);
        await host.Resolver.SecondQuerySeen.Task.WaitAsync(Timeout);

        // The first query is still inside the resolver; its bytes are what it arrived with.
        byte[][] seen = host.Resolver.RawMessages;
        Assert.NotSame(seen[0], seen[1]);
        Assert.Equal(firstBytes, seen[0]);
        Assert.Equal(secondBytes, seen[1]);

        host.Resolver.ReleaseFirstQuery();

        DnsMessage firstReply = DnsCryptWireTestHelper.DecryptResponsePacket(
            host.Certificate,
            firstSession,
            await ReceiveAsync(first),
            out _,
            out _
        );
        DnsMessage secondReply = DnsCryptWireTestHelper.DecryptResponsePacket(
            host.Certificate,
            secondSession,
            await ReceiveAsync(second),
            out _,
            out _
        );
        Assert.Equal(1, firstReply.Id);
        Assert.Equal(2, secondReply.Id);
    }

    [Fact]
    public async Task StopAsync_EndsTheReceiveLoopsAndLeavesNothingListening()
    {
        Host host = await Host.StartAsync();

        await host.DisposeAsync();

        Assert.False(host.Listener.IsListening);
    }

    private static async Task<byte[]> ReceiveAsync(UdpClient client)
    {
        using CancellationTokenSource timeout = new(Timeout);
        return (await client.ReceiveAsync(timeout.Token)).Buffer;
    }

    /// <summary>
    ///     This class owns one test's pipeline, certificate and DNSCrypt UDP listener on the IPv4
    ///     loopback address, started on a free port and stopped on dispose.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;

        public int Port { get; private init; }

        public DnsCryptUdpListener Listener { get; private set; }

        public DnsCryptCertificateEntry Certificate { get; private set; }

        public CapturingResolver Resolver { get; } = new();

        public static async Task<Host> StartAsync()
        {
            Host host = new() { Port = FreeUdpPort() };

            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    DnssecAlgorithmEnum.Ed25519,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                throw new InvalidOperationException("Ed25519 is not registered.");

            DnssecKeyPair providerKeyPair = algorithm.GenerateKeyPair();
            host.Certificate = DnsCryptListenerTestFixture.BuildSignedCertificate(
                algorithm,
                providerKeyPair,
                DnsCryptEsVersion.XChaCha20Poly1305
            );
            InMemoryDnsCryptCertificateStore store = new(providerKeyPair);
            store.Save([host.Certificate]);

            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([host.Resolver], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            host.Listener = new DnsCryptUdpListener(
                host._pipeline,
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(
                    new DnsCryptOptions
                    {
                        Enable = true,
                        Port = host.Port,
                        ListenAddresses = "127.0.0.1",
                        ProviderName = "example.test",
                    }
                ),
                NullLogger<DnsCryptUdpListener>.Instance
            );
            await host.Listener.StartAsync(CancellationToken.None);

            // BackgroundService runs ExecuteAsync (which binds) as a continuation, so wait for
            // the listener to report that it is receiving.
            for (int attempt = 0; attempt < 500 && !host.Listener.IsListening; attempt++)
                await Task.Delay(10);

            Assert.True(host.Listener.IsListening);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            // StopAsync returns only when ExecuteAsync, and so every receive loop, has ended.
            await Listener.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }

        private static int FreeUdpPort()
        {
            using Socket probe = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.LocalEndPoint).Port;
        }
    }

    /// <summary>
    ///     This resolver records the array each query's raw message is (the decrypted query the
    ///     listener handed over), holds the first query until the test releases it so a second
    ///     datagram can arrive while the first is still being processed, and answers every query
    ///     with an empty NOERROR response.
    /// </summary>
    private sealed class CapturingResolver : IDnsQueryResolver
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
        public byte[][] RawMessages => [.. _rawMessages];

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

            if (number == 1)
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
