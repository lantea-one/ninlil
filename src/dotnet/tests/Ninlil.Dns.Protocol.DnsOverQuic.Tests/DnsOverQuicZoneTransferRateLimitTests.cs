using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverQuic.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic.Tests;

/// <summary>
///     This class tests that <see cref="DnsOverQuicListener" /> applies the rate limiter to AXFR
///     queries (the counterpart of <c>TcpZoneTransferRateLimitTests</c> in the plain DNS
///     tests). It runs a real listener over loopback QUIC, a fake <see cref="IDnsRateLimiter" />,
///     a fake <see cref="IZoneTransferService" /> that counts its calls and a recording
///     <see cref="IDnsServerEventPublisher" /> standing for the query log. A denied transfer
///     must reset its stream with <c>DOQ_EXCESSIVE_LOAD</c> and no reply, never reach the
///     transfer service and publish no answered event; a served one publishes exactly one. Like
///     the other QUIC tests, each test returns early, reported as passed, when QUIC is not
///     supported on the host.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class DnsOverQuicZoneTransferRateLimitTests
{
    /// <summary>The DOQ_EXCESSIVE_LOAD error code (RFC 9250 §8.4).</summary>
    private const long DoqExcessiveLoad = 0x4;

    [Fact]
    public async Task AnAxfrBeyondTheRateLimit_ResetsTheStreamWithExcessiveLoadAndNoQueryLogEntry()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            return;

        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: 1, subnetDecision: DnsSubnetRateLimitDecisionEnum.Allow)
        );
        await using QuicConnection connection = await host.ConnectAsync();

        // The first transfer spends the whole budget and is served.
        await using (QuicStream served = await SendAxfrAsync(connection))
        {
            Assert.NotNull(await ReadMessageAsync(served));
            Assert.NotNull(await ReadMessageAsync(served));
            Assert.Null(await ReadMessageAsync(served));
        }

        // The second is over the limit: the server resets the stream without a reply.
        await using QuicStream denied = await SendAxfrAsync(connection);
        QuicException aborted = await Assert.ThrowsAsync<QuicException>(async () =>
            await ReadMessageAsync(denied)
        );

        Assert.Equal(QuicError.StreamAborted, aborted.QuicError);
        Assert.Equal(DoqExcessiveLoad, aborted.ApplicationErrorCode);
        Assert.Equal(1, service.Calls);
        Assert.Single(publisher.Events);
    }

    [Fact]
    public async Task AnAxfrTheSubnetLimitDrops_ResetsTheStreamAndDoesNotCallTheTransferService()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            return;

        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: int.MaxValue, DnsSubnetRateLimitDecisionEnum.Drop)
        );
        await using QuicConnection connection = await host.ConnectAsync();
        await using QuicStream stream = await SendAxfrAsync(connection);

        QuicException aborted = await Assert.ThrowsAsync<QuicException>(async () =>
            await ReadMessageAsync(stream)
        );

        Assert.Equal(DoqExcessiveLoad, aborted.ApplicationErrorCode);
        Assert.Equal(0, service.Calls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    public async Task AnAxfrWithinTheRateLimit_IsServedAndPublishesOneAnsweredEventForQuic()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            return;

        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: int.MaxValue, DnsSubnetRateLimitDecisionEnum.Allow)
        );
        await using QuicConnection connection = await host.ConnectAsync();
        await using QuicStream stream = await SendAxfrAsync(connection);

        Assert.Equal(1, (await ReadMessageAsync(stream)).Id);
        Assert.Equal(2, (await ReadMessageAsync(stream)).Id);
        Assert.Null(await ReadMessageAsync(stream));
        Assert.Equal(1, service.Calls);

        // The event is published after the last message is queued, which can be a moment after
        // the client has read it.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (publisher.Events.Count == 0)
            await Task.Delay(10, timeout.Token);

        DnsQueryAnsweredEvent answered = Assert.Single(publisher.Events);
        Assert.Equal(DnsTransportProtocolEnum.Quic, answered.Protocol);
        Assert.Equal(DnsResourceRecordTypeEnum.Axfr, answered.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.NoError, answered.ResponseCode);
        Assert.Equal(IPAddress.Loopback, answered.RemoteAddress);
    }

    /// <summary>
    ///     This method opens a stream and sends an AXFR query on it with message ID 0, as
    ///     RFC 9250 §4.2.1 requires, then closes the write side.
    /// </summary>
    private static async Task<QuicStream> SendAxfrAsync(QuicConnection connection)
    {
        QuicStream stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        byte[] message = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 0,
                Opcode = DnsOpcodeEnum.Query,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse("zone.example."),
                        DnsResourceRecordTypeEnum.Axfr,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );
        byte[] frame = new byte[2 + message.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)message.Length);
        message.CopyTo(frame, 2);
        await stream.WriteAsync(frame, completeWrites: true);
        return stream;
    }

    /// <summary>
    ///     This method reads one length-prefixed message, or returns <see langword="null" /> when
    ///     the server finished the stream cleanly. A reset surfaces as a
    ///     <see cref="QuicException" />.
    /// </summary>
    private static async Task<DnsMessage> ReadMessageAsync(QuicStream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        byte[] prefix = new byte[2];

        try
        {
            await stream.ReadExactlyAsync(prefix, timeout.Token);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        byte[] message = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await stream.ReadExactlyAsync(message, timeout.Token);
        return DnsMessageReader.Read(message, out _);
    }

    /// <summary>
    ///     This class starts a <see cref="DnsOverQuicListener" /> with a real
    ///     <see cref="DnsQueryProcessingPipeline" /> (unused by transfers, but the listener needs
    ///     one) on a loopback UDP port, with the given transfer service, publisher and limiter.
    /// </summary>
    private sealed class TransferHost : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsOverQuicListener _listener;
        private string _certificateStatePath;
        private int _port;

        /// <summary>
        ///     This method starts the host. <see cref="DnsOverQuicListener" /> does not expose the
        ///     port it bound, so a port 0 bind cannot be read back. Instead the test asks the OS
        ///     for a free UDP port and retries, at most five times, with a new port when another
        ///     process took it in between (the listener records that as a failed address).
        /// </summary>
        public static async Task<TransferHost> StartAsync(
            IZoneTransferService service,
            IDnsServerEventPublisher publisher,
            IDnsRateLimiter limiter
        )
        {
            TransferHost host = new()
            {
                _certificateStatePath = Path.Combine(
                    Path.GetTempPath(),
                    $"doq-transfer-{Guid.NewGuid():N}.pfx"
                ),
            };
            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([], new SilentPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            IServiceScopeFactory scopeFactory = new ServiceCollection()
                .AddSingleton(service)
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                host._port = LocalAddresses.GetFreePort();
                host._listener = new DnsOverQuicListener(
                    host._pipeline,
                    scopeFactory,
                    new StaticOptionsMonitor<DnsOverQuicOptions>(
                        new DnsOverQuicOptions
                        {
                            Enable = true,
                            Port = host._port,
                            ListenAddresses = "127.0.0.1",
                            Certificate = new TlsCertificateOptions
                            {
                                SelfSignedCertificateStatePath = host._certificateStatePath,
                                SelfSignedSubjectAlternativeNames = ["localhost"],
                            },
                        }
                    ),
                    NullLogger<DnsOverQuicListener>.Instance,
                    limiter,
                    publisher
                );
                await host._listener.StartAsync(CancellationToken.None);

                // The listener creates its self-signed certificate before it binds, which takes
                // longer on a busy machine than a fixed pause.
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

                while (
                    !host._listener.IsListening && host._listener.FailedListenAddresses.Count == 0
                )
                    await Task.Delay(10, timeout.Token);

                if (host._listener.IsListening)
                    return host;

                await host._listener.StopAsync(CancellationToken.None);
            }

            throw new InvalidOperationException("No free port could be bound after five tries.");
        }

        public async Task<QuicConnection> ConnectAsync() =>
            await QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, _port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol("doq")],
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    },
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                }
            );

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();

            if (File.Exists(_certificateStatePath))
                File.Delete(_certificateStatePath);
        }
    }

    /// <summary>
    ///     This limiter allows the first <c>permitted</c> per-address acquisitions, denies every
    ///     later one, and answers every subnet check with a fixed decision.
    /// </summary>
    private sealed class BudgetLimiter(int permitted, DnsSubnetRateLimitDecisionEnum subnetDecision)
        : IDnsRateLimiter
    {
        private int _used;

        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(Interlocked.Increment(ref _used) <= permitted);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(subnetDecision);
    }

    /// <summary>
    ///     This service counts its calls and yields two messages with ids 1 and 2.
    /// </summary>
    private sealed class CountingTransferService : IZoneTransferService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref _calls);
            await Task.CompletedTask;
            yield return new DnsMessage
            {
                Id = 1,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
            yield return new DnsMessage
            {
                Id = 2,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }

    /// <summary>
    ///     This publisher stands for the query log: it records every answered event.
    /// </summary>
    private sealed class RecordingPublisher : IDnsServerEventPublisher
    {
        private readonly List<DnsQueryAnsweredEvent> _events = [];

        public IReadOnlyList<DnsQueryAnsweredEvent> Events
        {
            get
            {
                lock (_events)
                    return [.. _events];
            }
        }

        public void Publish(IDnsServerEvent domainEvent)
        {
            if (domainEvent is DnsQueryAnsweredEvent answered)
                lock (_events)
                    _events.Add(answered);
        }
    }

    /// <summary>This publisher discards every event; the pipeline needs one and queries are not sent.</summary>
    private sealed class SilentPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
