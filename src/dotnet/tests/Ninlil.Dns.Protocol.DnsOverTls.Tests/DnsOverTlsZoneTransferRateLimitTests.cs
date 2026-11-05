using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverTls.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests;

/// <summary>
///     This class tests that <see cref="DnsOverTlsListener" /> applies the rate limiter to AXFR
///     queries (the counterpart of <c>TcpZoneTransferRateLimitTests</c> in the plain DNS
///     tests). It runs a real listener with a TLS handshake over a loopback TCP port, a fake
///     <see cref="IDnsRateLimiter" />, a fake <see cref="IZoneTransferService" /> that counts its
///     calls and a recording <see cref="IDnsServerEventPublisher" /> standing for the query log.
///     A denied transfer must close the connection with no reply, never reach the transfer
///     service and publish no answered event; a served one publishes exactly one.
/// </summary>
public sealed class DnsOverTlsZoneTransferRateLimitTests
{
    [Fact]
    public async Task AnAxfrBeyondTheRateLimit_ClosesTheConnectionWithoutAReplyOrAQueryLogEntry()
    {
        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: 1, subnetDecision: DnsSubnetRateLimitDecisionEnum.Allow)
        );

        // The first transfer spends the whole budget and is served.
        using SslStream first = await host.ConnectAsync();
        await WriteAxfrAsync(first, 1);
        Assert.NotNull(await ReadMessageOrEndAsync(first));
        Assert.NotNull(await ReadMessageOrEndAsync(first));
        Assert.Null(await ReadMessageOrEndAsync(first));

        // The second is over the limit: no reply, the server closes the connection.
        using SslStream second = await host.ConnectAsync();
        await WriteAxfrAsync(second, 2);
        Assert.Null(await ReadMessageOrEndAsync(second));

        Assert.Equal(1, service.Calls);
        Assert.Single(publisher.Events);
    }

    [Fact]
    public async Task AnAxfrTheSubnetLimitDrops_ClosesTheConnectionWithoutAReplyOrCallingTheTransferService()
    {
        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: int.MaxValue, DnsSubnetRateLimitDecisionEnum.Drop)
        );

        using SslStream stream = await host.ConnectAsync();
        await WriteAxfrAsync(stream, 3);

        Assert.Null(await ReadMessageOrEndAsync(stream));
        Assert.Equal(0, service.Calls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    public async Task AnAxfrWithinTheRateLimit_IsServedAndPublishesOneAnsweredEventForTls()
    {
        CountingTransferService service = new();
        RecordingPublisher publisher = new();
        await using TransferHost host = await TransferHost.StartAsync(
            service,
            publisher,
            new BudgetLimiter(permitted: int.MaxValue, DnsSubnetRateLimitDecisionEnum.Allow)
        );

        using SslStream stream = await host.ConnectAsync();
        await WriteAxfrAsync(stream, 4);

        Assert.Equal(1, (await ReadMessageOrEndAsync(stream)).Id);
        Assert.Equal(2, (await ReadMessageOrEndAsync(stream)).Id);
        Assert.Null(await ReadMessageOrEndAsync(stream));
        Assert.Equal(1, service.Calls);

        DnsQueryAnsweredEvent answered = Assert.Single(publisher.Events);
        Assert.Equal(DnsTransportProtocolEnum.Tls, answered.Protocol);
        Assert.Equal(DnsResourceRecordTypeEnum.Axfr, answered.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.NoError, answered.ResponseCode);
        Assert.Equal(IPAddress.Loopback, answered.RemoteAddress);
    }

    private static async Task WriteAxfrAsync(Stream stream, ushort id)
    {
        byte[] message = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = id,
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
        await stream.WriteAsync(frame);
    }

    /// <summary>
    ///     This method reads one length-prefixed message, or returns <see langword="null" /> when
    ///     the server ended the connection (an orderly close or a reset).
    /// </summary>
    private static async Task<DnsMessage> ReadMessageOrEndAsync(Stream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        byte[] prefix = new byte[2];

        try
        {
            await stream.ReadExactlyAsync(prefix, timeout.Token);
            byte[] message = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
            await stream.ReadExactlyAsync(message, timeout.Token);
            return DnsMessageReader.Read(message, out _);
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This class starts a <see cref="DnsOverTlsListener" /> with a real
    ///     <see cref="DnsQueryProcessingPipeline" /> (unused by transfers, but the listener needs
    ///     one) on a loopback port, with the given transfer service, publisher and limiter.
    /// </summary>
    private sealed class TransferHost : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsOverTlsListener _listener;
        private int _port;

        /// <summary>
        ///     This method starts the host on port 0 and reads the port the listener bound.
        /// </summary>
        public static async Task<TransferHost> StartAsync(
            IZoneTransferService service,
            IDnsServerEventPublisher publisher,
            IDnsRateLimiter limiter
        )
        {
            TransferHost host = new();
            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([], new SilentPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            host._listener = new DnsOverTlsListener(
                host._pipeline,
                new ServiceCollection()
                    .AddSingleton(service)
                    .BuildServiceProvider()
                    .GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<DnsOverTlsOptions>(
                    new DnsOverTlsOptions
                    {
                        Enable = true,
                        Port = 0,
                        ListenAddresses = "127.0.0.1",
                        IdleTimeout = TimeSpan.FromSeconds(30),
                    }
                ),
                NullLogger<DnsOverTlsListener>.Instance,
                limiter,
                publisher
            );
            await host._listener.StartAsync(CancellationToken.None);

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

            while (!host._listener.IsListening)
                await Task.Delay(10, timeout.Token);

            host._port = host._listener.BoundPort;
            return host;
        }

        public async Task<SslStream> ConnectAsync()
        {
            TcpClient client = new(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, _port);
            SslStream stream = new(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await stream.AuthenticateAsClientAsync("localhost");
            return stream;
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
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
