using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverTls.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests;

/// <summary>
///     This class tests the connection-level limits of the DNS-over-TLS listener, which it gets
///     from <c>FramedDnsStreamSession</c>: the TLS handshake timeout, pipelined queries, the write
///     deadline for a client that stops reading, and a zone transfer that runs longer than the
///     idle timeout. Every test uses real loopback TCP and TLS with the self-signed certificate
///     the listener generates, and a short idle timeout (400 ms) that doubles as the handshake
///     timeout and the write deadline. Waits are bounded by a generous safety timeout, so only the
///     checks that something did not happen depend on elapsed time. A client that must succeed
///     retries its connection until it is served, because that short timeout is also the TLS
///     handshake deadline.
/// </summary>
public sealed class DnsOverTlsSessionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The longest a client that must succeed keeps connecting before the test gives up.
    /// </summary>
    private static readonly TimeSpan ServedDeadline = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task AClientThatNeverSendsAClientHello_IsClosedAtTheHandshakeTimeout_AndOthersAreServed()
    {
        await using Host host = await Host.StartAsync(maxConnections: 4);

        using TcpClient silent = new();
        await silent.ConnectAsync(IPAddress.Loopback, host.Port);
        Stopwatch stopwatch = Stopwatch.StartNew();

        // While the silent client holds a connection, a proper client is answered.
        DnsMessage answer = await ConnectUntilServedAsync(host, 1, "healthy.example.");
        Assert.Equal(1, answer.Id);

        // The server closes the silent connection once the lowered handshake timeout passes.
        using CancellationTokenSource timeout = new(SafetyTimeout);
        int read = await ReadToEndAsync(silent.GetStream(), timeout.Token);

        Assert.Equal(0, read);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task ATimedOutHandshake_FreesItsConnectionSlot()
    {
        await using Host host = await Host.StartAsync(maxConnections: 1);

        using TcpClient silent = new();
        await silent.ConnectAsync(IPAddress.Loopback, host.Port);

        using CancellationTokenSource timeout = new(SafetyTimeout);
        await ReadToEndAsync(silent.GetStream(), timeout.Token);

        // The listener ends the stream before it releases the connection slot, so the end of the
        // stream does not yet mean the slot is free. A client that connects in between is closed
        // at once; like a real client it connects again until it is served.
        DnsMessage answer = await ConnectUntilServedAsync(
            host,
            2,
            "after-handshake-timeout.example."
        );
        Assert.Equal(2, answer.Id);
    }

    [Fact]
    public async Task ManyPipelinedQueriesOnOneTlsConnection_AreAllAnswered()
    {
        await using Host host = await Host.StartAsync();
        using SslStream client = await ConnectWithRetryAsync(host);

        const int count = 40;

        for (ushort id = 0; id < count; id++)
            await WriteQueryAsync(client, BuildQuery(id, $"pipelined-{id}.example."));

        HashSet<ushort> answered = [];

        for (int index = 0; index < count; index++)
            answered.Add((await ReadResponseAsync(client)).Id);

        Assert.Equal(count, answered.Count);
    }

    [Fact]
    public async Task AClientThatStopsReading_IsCutByTheWriteDeadline_WithoutHurtingOthers()
    {
        TaskCompletionSource transferEnded = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        BulkZoneTransferService.TransferEnded = transferEnded;
        await using Host host = await Host.StartAsync(
            transferService: typeof(BulkZoneTransferService)
        );

        // The handshake deadline is the 400 ms idle timeout, which a loaded host can exceed for
        // a client that must succeed, so the stalled client retries its handshake too.
        using SslStream stalledStream = await ConnectWithRetryAsync(host, receiveBufferSize: 4096);

        // The transfer yields far more than the socket buffers hold; this client never reads.
        await WriteQueryAsync(stalledStream, BuildAxfr(5));

        // Another client is answered while the first one is stuck.
        DnsMessage answer = await ConnectUntilServedAsync(host, 6, "other.example.");
        Assert.Equal(6, answer.Id);

        // The write deadline ends the connection, which cancels the transfer long before it
        // would have produced every message.
        await transferEnded.Task.WaitAsync(SafetyTimeout);
        Assert.True(BulkZoneTransferService.Yielded < BulkZoneTransferService.TotalMessages);
    }

    [Fact]
    public async Task AnAxfr_ThatTakesLongerThanTheIdleTimeout_Completes()
    {
        await using Host host = await Host.StartAsync(typeof(SlowZoneTransferService));
        using SslStream client = await ConnectWithRetryAsync(host);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await WriteQueryAsync(client, BuildAxfr(9));

        List<ushort> ids = [];

        for (int index = 0; index < SlowZoneTransferService.MessageCount; index++)
            ids.Add((await ReadResponseAsync(client)).Id);

        Assert.Equal([1, 2, 3], ids);
        Assert.True(stopwatch.Elapsed > IdleTimeout * 3);

        // The server closes the connection after a transfer.
        using CancellationTokenSource timeout = new(SafetyTimeout);
        Assert.Equal(0, await ReadToEndAsync(client, timeout.Token));
    }

    /// <summary>
    ///     This method connects, queries and reads the answer, and starts again with a new
    ///     connection when the server cuts the attempt. The class runs with a 400 ms idle timeout
    ///     that the listener also uses as the TLS handshake deadline, and a closed slot or a slow
    ///     handshake on a busy host cuts a client that is behaving. A DoT client retries after a
    ///     reset, so retrying tests the guarantee (the client is served) without a race.
    /// </summary>
    /// <param name="host">The listener to query.</param>
    /// <param name="id">The message id of the query and the expected answer.</param>
    /// <param name="name">The query name.</param>
    /// <returns>The answer to the first attempt that gets one.</returns>
    /// <exception cref="TimeoutException">
    ///     No attempt was served within <see cref="ServedDeadline" />; the last failure is the
    ///     inner exception.
    /// </exception>
    private static async Task<DnsMessage> ConnectUntilServedAsync(Host host, ushort id, string name)
    {
        using CancellationTokenSource deadline = new(ServedDeadline);
        Exception last = null;

        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using SslStream client = await host.ConnectAsync(deadline.Token);
                await WriteQueryAsync(client, BuildQuery(id, name));
                return await ReadResponseAsync(client);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or AuthenticationException
                            or SocketException
                            or EndOfStreamException
                )
            {
                last = exception;
            }
        }

        throw new TimeoutException("No connection was served before the deadline.", last);
    }

    /// <summary>
    ///     This method opens a TLS connection and retries the connect and handshake, as
    ///     <see cref="ConnectUntilServedAsync" /> does, for a client that sends its query itself.
    /// </summary>
    /// <param name="host">The listener to connect to.</param>
    /// <param name="receiveBufferSize">The client receive buffer, or 0 for the default.</param>
    /// <exception cref="TimeoutException">
    ///     No handshake finished within <see cref="ServedDeadline" />.
    /// </exception>
    private static async Task<SslStream> ConnectWithRetryAsync(Host host, int receiveBufferSize = 0)
    {
        using CancellationTokenSource deadline = new(ServedDeadline);
        Exception last = null;

        while (!deadline.IsCancellationRequested)
        {
            try
            {
                return await host.ConnectAsync(deadline.Token, receiveBufferSize);
            }
            catch (Exception exception)
                when (exception is IOException or AuthenticationException or SocketException)
            {
                last = exception;
            }
        }

        throw new TimeoutException("No handshake finished before the deadline.", last);
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

    private static DnsMessage BuildAxfr(ushort id) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("transfer.example."),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };

    private static async Task WriteQueryAsync(Stream stream, DnsMessage query)
    {
        byte[] messageBytes = DnsMessageWriter.Write(query);
        byte[] frame = new byte[2 + messageBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)messageBytes.Length);
        messageBytes.CopyTo(frame.AsSpan(2));
        await stream.WriteAsync(frame);
    }

    private static async Task<DnsMessage> ReadResponseAsync(Stream stream)
    {
        using CancellationTokenSource timeout = new(SafetyTimeout);
        byte[] lengthPrefix = new byte[2];
        await stream.ReadExactlyAsync(lengthPrefix, timeout.Token);

        byte[] messageBytes = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await stream.ReadExactlyAsync(messageBytes, timeout.Token);
        return DnsMessageReader.Read(messageBytes, out _);
    }

    /// <summary>
    ///     This method reads until the server ends the connection, whether as a clean close or a
    ///     reset, and returns how many octets arrived before that.
    /// </summary>
    private static async Task<int> ReadToEndAsync(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[256];
        int total = 0;

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, token);

                if (read == 0)
                    return total;

                total += read;
            }
        }
        catch (IOException)
        {
            return total;
        }
    }

    /// <summary>
    ///     This class is a transfer service that yields large messages, so a client that never
    ///     reads fills every buffer between the service and itself.
    /// </summary>
    private sealed class BulkZoneTransferService : IZoneTransferService
    {
        public const int TotalMessages = 600;
        private static int _yielded;

        public static TaskCompletionSource TransferEnded { get; set; }

        public static int Yielded => Volatile.Read(ref _yielded);

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            Volatile.Write(ref _yielded, 0);

            try
            {
                for (int index = 0; index < TotalMessages; index++)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _yielded);

                    DnsMessage message = new() { Id = (ushort)index, IsResponse = true };
                    message.Answers.Add(
                        new DnsResourceRecord(
                            DnsName.Parse("bulk.example."),
                            DnsResourceRecordTypeEnum.Txt,
                            DnsClassEnum.Internet,
                            60,
                            new byte[40_000]
                        )
                    );
                    yield return message;
                    await Task.Yield();
                }
            }
            finally
            {
                TransferEnded?.TrySetResult();
            }
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }

    /// <summary>
    ///     This class is a transfer service whose messages are further apart than the idle
    ///     timeout, so the whole transfer outlasts it several times.
    /// </summary>
    private sealed class SlowZoneTransferService : IZoneTransferService
    {
        public const int MessageCount = 3;

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            for (ushort id = 1; id <= MessageCount; id++)
            {
                await Task.Delay(IdleTimeout * 1.5, stoppingToken);
                yield return new DnsMessage { Id = id, IsResponse = true };
            }
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }

    /// <summary>
    ///     This resolver answers every query with an empty NOERROR response.
    /// </summary>
    private sealed class EchoResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) =>
            ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        Questions = { context.Request.Questions[0] },
                    }
                )
            );
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    /// <summary>
    ///     This class owns a pipeline and a listener on a free port, for one test.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsOverTlsListener _listener;

        public int Port { get; private set; }

        public static async Task<Host> StartAsync(
            Type transferService = null,
            int maxConnections = 16
        )
        {
            Host host = new();
            using (TcpListener probe = new(IPAddress.Loopback, 0))
            {
                probe.Start();
                host.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }

            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([new EchoResolver()], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            ServiceCollection services = new();
            services.AddScoped(
                typeof(IZoneTransferService),
                transferService ?? typeof(SlowZoneTransferService)
            );

            host._listener = new DnsOverTlsListener(
                host._pipeline,
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<DnsOverTlsOptions>(
                    new DnsOverTlsOptions
                    {
                        Enable = true,
                        Port = host.Port,
                        IdleTimeout = IdleTimeout,
                        MaxConcurrentConnections = maxConnections,
                    }
                ),
                NullLogger<DnsOverTlsListener>.Instance
            );
            await host._listener.StartAsync(CancellationToken.None);

            // ExecuteAsync binds as a continuation of StartAsync; wait until it is listening.
            for (int attempt = 0; attempt < 500 && !host._listener.IsListening; attempt++)
                await Task.Delay(10);

            return host;
        }

        /// <summary>
        ///     This method connects and completes the TLS handshake. A failed attempt disposes
        ///     its socket before the exception goes on.
        /// </summary>
        /// <param name="stoppingToken">Cancels the connect and the handshake.</param>
        /// <param name="receiveBufferSize">The client receive buffer, or 0 for the default.</param>
        public async Task<SslStream> ConnectAsync(
            CancellationToken stoppingToken = default,
            int receiveBufferSize = 0
        )
        {
            TcpClient client = new();

            try
            {
                if (receiveBufferSize > 0)
                    client.ReceiveBufferSize = receiveBufferSize;

                await client.ConnectAsync(IPAddress.Loopback, Port, stoppingToken);
                SslStream stream = new(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    (_, _, _, _) => true
                );
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = "localhost" },
                    stoppingToken
                );
                return stream;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }
}
