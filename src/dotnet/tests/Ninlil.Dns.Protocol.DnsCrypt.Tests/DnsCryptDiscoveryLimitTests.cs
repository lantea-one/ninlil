using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class tests that a plaintext certificate discovery query passes the rate limiter like
///     any other query, and that a UDP discovery answer respects the response-to-query size
///     ceiling. Each test starts its own listeners on a free loopback port with a fake
///     <see cref="IDnsRateLimiter" />, since the shared <see cref="DnsCryptListenerTestFixture" />
///     has no limiter.
/// </summary>
public sealed class DnsCryptDiscoveryLimitTests
{
    [Fact]
    public async Task Udp_ADeniedDiscoveryQueryGetsNoReply()
    {
        FakeRateLimiter limiter = new(allow: false);
        await using RunningListeners running = await RunningListeners.StartAsync(limiter);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);
        await client.SendAsync(BuildDiscoveryQuery());

        Assert.Null(await TryReceiveAsync(client));
        Assert.Equal(1, limiter.AcquireCount);
    }

    [Fact]
    public async Task Udp_ADiscoveryQueryInADroppedSubnetGetsNoReply()
    {
        FakeRateLimiter limiter = new(subnetDecision: DnsSubnetRateLimitDecisionEnum.Drop);
        await using RunningListeners running = await RunningListeners.StartAsync(limiter);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);
        await client.SendAsync(BuildDiscoveryQuery());

        Assert.Null(await TryReceiveAsync(client));
        Assert.True(limiter.LastSubnetCheckWasUdp);
    }

    [Fact]
    public async Task Udp_ASubnetThatShouldRetryOverTcpGetsATruncatedReply()
    {
        FakeRateLimiter limiter = new(
            subnetDecision: DnsSubnetRateLimitDecisionEnum.RespondTruncated
        );
        await using RunningListeners running = await RunningListeners.StartAsync(limiter);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);
        await client.SendAsync(BuildDiscoveryQuery(id: 21));

        DnsMessage reply = DnsMessageReader.Read(await ReceiveReplyAsync(client));

        Assert.Equal(21, reply.Id);
        Assert.True(reply.IsTruncated);
        Assert.Empty(reply.Answers);
    }

    [Fact]
    public async Task Udp_AnAllowedDiscoveryQueryGetsTheCertificates()
    {
        FakeRateLimiter limiter = new();
        await using RunningListeners running = await RunningListeners.StartAsync(limiter);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);
        await client.SendAsync(BuildDiscoveryQuery());

        byte[] reply = await ReceiveReplyAsync(client);

        Assert.NotNull(reply);
        Assert.Equal(
            2,
            DnsCryptWireTestHelper.ExtractCertificatesFromDiscoveryResponse(reply).Count
        );
        Assert.Equal(1, limiter.AcquireCount);
    }

    [Fact]
    public async Task Udp_ADiscoveryReplyOverTheSizeCeilingIsTruncatedAndNoLargerThanTheQuery()
    {
        // A factor of 1 and no floor limit a reply to the size of its query, which two
        // certificates exceed several times over.
        DnsQueryProcessingPipelineOptions pipelineOptions = new()
        {
            MaxUdpAmplificationFactor = 1,
            MinUdpAmplificationFloorSizeOctets = 0,
        };
        await using RunningListeners running = await RunningListeners.StartAsync(
            new FakeRateLimiter(),
            pipelineOptions
        );

        byte[] query = BuildDiscoveryQuery(id: 22);
        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);
        await client.SendAsync(query);

        byte[] replyBytes = await ReceiveReplyAsync(client);
        DnsMessage reply = DnsMessageReader.Read(replyBytes);

        Assert.True(replyBytes.Length <= query.Length);
        Assert.Equal(22, reply.Id);
        Assert.True(reply.IsTruncated);
        Assert.Empty(reply.Answers);
    }

    [Fact]
    public async Task Tcp_ADeniedDiscoveryQueryGetsNoReply()
    {
        FakeRateLimiter limiter = new(allow: false);
        await using RunningListeners running = await RunningListeners.StartAsync(limiter);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, running.Port);
        using NetworkStream stream = client.GetStream();
        await WriteFramedAsync(stream, BuildDiscoveryQuery());

        using CancellationTokenSource timeout = new(SilenceWindow);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await stream.ReadAtLeastAsync(new byte[2], 1, true, timeout.Token)
        );
        Assert.Equal(1, limiter.AcquireCount);
    }

    [Fact]
    public async Task Tcp_ADiscoveryReplyIsNotLimitedToTheSizeCeiling()
    {
        // The strict ceiling would truncate the reply over UDP; over TCP it must not apply.
        DnsQueryProcessingPipelineOptions pipelineOptions = new()
        {
            MaxUdpAmplificationFactor = 1,
            MinUdpAmplificationFloorSizeOctets = 0,
        };
        FakeRateLimiter limiter = new();
        await using RunningListeners running = await RunningListeners.StartAsync(
            limiter,
            pipelineOptions
        );

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, running.Port);
        using NetworkStream stream = client.GetStream();
        await WriteFramedAsync(stream, BuildDiscoveryQuery());

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        byte[] lengthPrefix = new byte[2];
        await stream.ReadExactlyAsync(lengthPrefix, timeout.Token);
        byte[] reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await stream.ReadExactlyAsync(reply, timeout.Token);

        Assert.Equal(
            2,
            DnsCryptWireTestHelper.ExtractCertificatesFromDiscoveryResponse(reply).Count
        );
        Assert.False(limiter.LastSubnetCheckWasUdp);
    }

    private static byte[] BuildDiscoveryQuery(ushort id = 1)
    {
        DnsMessage query = new() { Id = id, Opcode = DnsOpcodeEnum.Query };
        query.Questions.Add(
            new DnsQuestion(
                DnsName.Parse($"2.dnscrypt-cert.{DnsCryptListenerTestFixture.ProviderName}"),
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Internet
            )
        );
        return DnsMessageWriter.Write(query);
    }

    /// <summary>
    ///     This constant is how long a test waits for a reply that must come. It is a failure
    ///     bound, not a delay: the wait ends when the datagram arrives, so a loaded machine that
    ///     answers late still passes.
    /// </summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This constant is how long a test waits to confirm that no reply comes. A silent
    ///     listener always costs the whole window, so it stays short; a stalled machine can only
    ///     make such a test pass, never fail.
    /// </summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     This method waits up to <see cref="ReplyTimeout" /> for a datagram that must arrive
    ///     and fails the test when none does.
    /// </summary>
    private static async Task<byte[]> ReceiveReplyAsync(UdpClient client)
    {
        using CancellationTokenSource timeout = new(ReplyTimeout);

        try
        {
            return (await client.ReceiveAsync(timeout.Token)).Buffer;
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"No reply arrived within {ReplyTimeout}.");
            return null;
        }
    }

    /// <summary>
    ///     This method waits <see cref="SilenceWindow" /> for a datagram and returns
    ///     <see langword="null" /> when none arrives.
    /// </summary>
    private static async Task<byte[]> TryReceiveAsync(UdpClient client)
    {
        using CancellationTokenSource timeout = new(SilenceWindow);

        try
        {
            return (await client.ReceiveAsync(timeout.Token)).Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task WriteFramedAsync(NetworkStream stream, byte[] payload)
    {
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)payload.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(payload);
    }

    /// <summary>
    ///     This class is an <see cref="IDnsRateLimiter" /> with fixed answers that records how it
    ///     was called.
    /// </summary>
    private sealed class FakeRateLimiter(
        bool allow = true,
        DnsSubnetRateLimitDecisionEnum subnetDecision = DnsSubnetRateLimitDecisionEnum.Allow
    ) : IDnsRateLimiter
    {
        private int _acquireCount;

        public int AcquireCount => Volatile.Read(ref _acquireCount);

        public bool LastSubnetCheckWasUdp { get; private set; }

        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref _acquireCount);
            return ValueTask.FromResult(allow);
        }

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        )
        {
            LastSubnetCheckWasUdp = isUdp;
            return ValueTask.FromResult(subnetDecision);
        }
    }

    /// <summary>
    ///     This class starts a pipeline plus both DNSCrypt listeners on a free port, with signed
    ///     certificates for both ciphers and the given limiter, and stops them when disposed.
    /// </summary>
    private sealed class RunningListeners : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsCryptUdpListener _udp;
        private DnsCryptTcpListener _tcp;

        public int Port { get; private set; }

        public static async Task<RunningListeners> StartAsync(
            IDnsRateLimiter limiter,
            DnsQueryProcessingPipelineOptions pipelineOptions = null
        )
        {
            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    DnssecAlgorithmEnum.Ed25519,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                throw new InvalidOperationException("Ed25519 is not registered.");

            DnssecKeyPair providerKeyPair = algorithm.GenerateKeyPair();
            InMemoryDnsCryptCertificateStore store = new(providerKeyPair);
            store.Save([
                DnsCryptListenerTestFixture.BuildSignedCertificate(
                    algorithm,
                    providerKeyPair,
                    DnsCryptEsVersion.XChaCha20Poly1305
                ),
                DnsCryptListenerTestFixture.BuildSignedCertificate(
                    algorithm,
                    providerKeyPair,
                    DnsCryptEsVersion.XSalsa20Poly1305
                ),
            ]);

            RunningListeners running = new();
            DnsCryptOptions dnscryptOptions = new()
            {
                Enable = true,
                ProviderName = DnsCryptListenerTestFixture.ProviderName,
                ListenAddresses = "127.0.0.1",
            };
            StaticOptionsMonitor<DnsCryptOptions> options = new(dnscryptOptions);
            StaticOptionsMonitor<DnsQueryProcessingPipelineOptions> pipelineOptionsMonitor = new(
                pipelineOptions ?? new DnsQueryProcessingPipelineOptions()
            );

            running._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher(
                    [new EchoResolver()],
                    new DnsServerEventPublisher(
                        Options.Create(new DnsServerEventPublisherOptions()),
                        NullLogger<DnsServerEventPublisher>.Instance
                    )
                ),
                pipelineOptionsMonitor,
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await running._pipeline.StartAsync(CancellationToken.None);

            (running._udp, running._tcp, int port) =
                await DnsCryptListenerStartup.StartOnFreePortAsync(
                    dnscryptOptions,
                    () =>
                        (
                            new DnsCryptUdpListener(
                                running._pipeline,
                                store,
                                options,
                                NullLogger<DnsCryptUdpListener>.Instance,
                                limiter,
                                pipelineOptionsMonitor
                            ),
                            new DnsCryptTcpListener(
                                running._pipeline,
                                store,
                                options,
                                NullLogger<DnsCryptTcpListener>.Instance,
                                limiter
                            )
                        )
                );
            running.Port = port;
            return running;
        }

        public async ValueTask DisposeAsync()
        {
            if (_udp is not null)
                await _udp.StopAsync(CancellationToken.None);

            if (_tcp is not null)
                await _tcp.StopAsync(CancellationToken.None);

            if (_pipeline is not null)
                await _pipeline.StopAsync(CancellationToken.None);
        }
    }
}
