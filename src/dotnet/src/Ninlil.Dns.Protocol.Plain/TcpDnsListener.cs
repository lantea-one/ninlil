using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class implements RFC 1035 §4.2.2/RFC 7766's DNS-over-TCP transport: a two-octet
///     big-endian length prefix ahead of every message, one connection potentially carrying many
///     queries in sequence (RFC 7766 §6.2.1 pipelining). An AXFR/IXFR query is recognized here and
///     answered directly through <see cref="IZoneTransferService" />'s own multi-message
///     sequence, since <see cref="DnsQueryProcessingPipeline" /> can only ever represent a single
///     request producing a single response; every other query still goes through that same
///     shared pipeline, exactly as <see cref="UdpDnsListener" /> already does.
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">This transport's own listen port and connection limits.</param>
/// <param name="logger">The logger this listener records connection-level failures to.</param>
public sealed class TcpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptions<PlainProtocolOptions> options,
    ILogger<TcpDnsListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This property contains the listening socket, once <see cref="ExecuteAsync" /> has
    ///     bound one -- kept here so <see cref="StopAsync" /> can close it, which is what
    ///     actually unblocks the accept loop's own pending
    ///     <see cref="Socket.AcceptAsync(CancellationToken)" /> call during shutdown.
    /// </summary>
    private Socket _listenSocket;

    /// <summary>
    ///     This property bounds how many connections may be accepted concurrently, per
    ///     <see cref="PlainProtocolOptions.MaxConcurrentTcpConnections" />.
    /// </summary>
    private SemaphoreSlim _connectionLimiter;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting connections -- read by the <c>/status</c> health-check endpoint, which needs
    ///     a real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connectionLimiter = new SemaphoreSlim(options.Value.MaxConcurrentTcpConnections);

        _listenSocket = new Socket(
            AddressFamily.InterNetworkV6,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        _listenSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Value.Port));
        _listenSocket.Listen();
        IsListening = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket connection;

            try
            {
                connection = await _listenSocket.AcceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // Cancelling a pending AcceptAsync (via StopAsync closing the listening socket,
                // or the token itself) surfaces here as a SocketException on this platform/
                // runtime combination, not as OperationCanceledException/ObjectDisposedException
                // the way a cleaner cancellation would -- observed directly against a real
                // shutdown in this project's own smoke test, which is exactly the scenario every
                // graceful host shutdown goes through. Treated the same way: if we were asked to
                // stop, this is that; otherwise, keep serving rather than taking the whole
                // listener down over one accept failure.
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                // Too many connections already open; reject this one immediately rather than
                // leaving it to queue behind every other connection this server is already
                // struggling to keep up with.
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method owns a single accepted connection for its entire lifetime: reading every
    ///     length-prefixed query it sends, answering each in turn, and closing the connection
    ///     once RFC 7766's idle timeout elapses, the client disconnects, or a zone transfer this
    ///     connection just served completes.
    ///     <para>
    ///         That last case is deliberate, not an accident of RFC 7766 §6.2.1 pipelining support:
    ///         <see cref="ZoneTransferPullOverStream.ReadResponsesAsync" />'s own reassembly loop
    ///         on the *client* side of a transfer (this project's own <c>TcpZoneTransferClient</c>,
    ///         used by <c>SecondaryZoneRefreshService</c>, and confirmed against a real, external
    ///         reference client too) has no way to know a transfer response is complete other than
    ///         the connection actually closing -- there is no length-prefixed "end of transfer"
    ///         sentinel in RFC 5936/1995's own wire format, and a single-message AXFR reply (the
    ///         common case for a small zone) carries no repeated closing SOA to self-terminate on,
    ///         unlike a multi-message one. Leaving the connection open afterward for further
    ///         pipelined queries -- technically legal, and what this method used to do
    ///         unconditionally -- left every such client hanging until its own read-timeout fired,
    ///         a real bug discovered only by a genuine two-process zone-transfer smoke test driven
    ///         against separate, real listener processes, not by any unit/integration test run so
    ///         far (none of which exercised a real, separate-process TCP transfer end-to-end). An
    ///         ordinary, non-transfer query
    ///         still keeps the connection open for pipelining exactly as before.
    ///     </para>
    /// </summary>
    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken)
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;
        SemaphoreSlim writeLock = new(1, 1);

        try
        {
            using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken
            );

            while (true)
            {
                idleCts.CancelAfter(options.Value.TcpIdleTimeout);

                byte[] lengthPrefix = new byte[2];

                if (!await ReadExactAsync(connection, lengthPrefix, idleCts.Token))
                    break;

                ushort messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
                byte[] messageBuffer = new byte[messageLength];

                if (!await ReadExactAsync(connection, messageBuffer, idleCts.Token))
                    break;

                bool wasZoneTransfer = await HandleMessageAsync(
                    messageBuffer,
                    remoteEndpoint.Address,
                    connection,
                    writeLock,
                    idleCts.Token
                );

                if (wasZoneTransfer)
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "TCP connection from {Endpoint} ended abnormally.",
                remoteEndpoint
            );
        }
        finally
        {
            connection.Close();
            _connectionLimiter.Release();
        }
    }

    /// <summary>
    ///     This method decodes a single TCP-framed message and routes it: an AXFR/IXFR query is
    ///     answered directly, as a real multi-message sequence, through
    ///     <see cref="IZoneTransferService" />; every other query is enqueued onto the shared
    ///     <see cref="DnsQueryProcessingPipeline" />, the same as any UDP query.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if this connection just finished serving a zone transfer and
    ///     should now be closed rather than kept open for further RFC 7766 §6.2.1 pipelined
    ///     queries -- see <see cref="HandleConnectionAsync" />'s own doc comment on why a transfer
    ///     response, specifically, needs this. <see langword="false" /> for every ordinary query,
    ///     which keeps the connection open exactly as before.
    /// </returns>
    private async Task<bool> HandleMessageAsync(
        byte[] messageBytes,
        IPAddress remoteAddress,
        Socket connection,
        SemaphoreSlim writeLock,
        CancellationToken stoppingToken
    )
    {
        DnsMessage request;
        int tsigRecordStartOffset;

        try
        {
            request = DnsMessageReader.Read(messageBytes, out tsigRecordStartOffset);
        }
        catch (FormatException exception)
        {
            // A malformed message has no reliable identifier to answer with, the same reasoning
            // DnsQueryProcessingPipeline itself already applies to a malformed UDP datagram.
            logger.LogWarning(
                exception,
                "Discarding a malformed TCP DNS message from {RemoteAddress}.",
                remoteAddress
            );
            return false;
        }

        DnsResourceRecordTypeEnum? questionType =
            request.Questions.Count > 0 ? request.Questions[0].Type : null;

        if (questionType is DnsResourceRecordTypeEnum.Axfr or DnsResourceRecordTypeEnum.Ixfr)
        {
            await HandleZoneTransferAsync(
                request,
                messageBytes,
                tsigRecordStartOffset,
                remoteAddress,
                questionType.Value,
                connection,
                writeLock,
                stoppingToken
            );
            return true;
        }

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                messageBytes,
                DnsTransportProtocolEnum.Tcp,
                remoteAddress,
                (responseBytes, sendToken) =>
                    WriteFramedAsync(connection, writeLock, responseBytes, sendToken)
            ),
            stoppingToken
        );

        return false;
    }

    /// <summary>
    ///     This method streams a complete AXFR or IXFR response back, one length-prefixed
    ///     message at a time, in the exact order <see cref="IZoneTransferService" /> yields them.
    /// </summary>
    private async Task HandleZoneTransferAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsResourceRecordTypeEnum questionType,
        Socket connection,
        SemaphoreSlim writeLock,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneTransferService transferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsQueryContext context = new(
            request,
            DnsTransportProtocolEnum.Tcp,
            remoteAddress,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigRecordStartOffset
        );

        IAsyncEnumerable<DnsMessage> responses =
            questionType == DnsResourceRecordTypeEnum.Axfr
                ? transferService.CreateAxfrResponseAsync(context, stoppingToken)
                : transferService.CreateIxfrResponseAsync(context, stoppingToken);

        await foreach (DnsMessage responseMessage in responses.WithCancellation(stoppingToken))
            await WriteFramedAsync(
                connection,
                writeLock,
                DnsMessageWriter.Write(responseMessage),
                stoppingToken
            );
    }

    /// <summary>
    ///     This method writes a single length-prefixed message to a connection, serialized
    ///     against every other write on that same connection -- necessary because
    ///     <see cref="DnsQueryProcessingPipeline" /> may finish answering two pipelined queries on
    ///     the same connection out of order, and an interleaved write would corrupt the framing
    ///     for both.
    /// </summary>
    private static async ValueTask WriteFramedAsync(
        Socket connection,
        SemaphoreSlim writeLock,
        ReadOnlyMemory<byte> messageBytes,
        CancellationToken stoppingToken
    )
    {
        await writeLock.WaitAsync(stoppingToken);

        try
        {
            byte[] lengthPrefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)messageBytes.Length);

            await connection.SendAsync(lengthPrefix, stoppingToken);
            await connection.SendAsync(messageBytes, stoppingToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    ///     This method fills a buffer completely from a socket, or reports a clean disconnect.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> once <paramref name="buffer" /> is completely filled, or
    ///     <see langword="false" /> if the connection closed before the first byte of this read
    ///     arrived (a clean disconnect between messages, not a truncated one).
    /// </returns>
    /// <exception cref="IOException">
    ///     Thrown when the connection closes partway through an already-started read -- a
    ///     genuinely truncated, malformed TCP stream, as opposed to an ordinary disconnect
    ///     between messages.
    /// </exception>
    private static async ValueTask<bool> ReadExactAsync(
        Socket connection,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await connection.ReceiveAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
            {
                if (totalRead == 0)
                    return false;

                throw new IOException("The connection closed partway through a DNS message.");
            }

            totalRead += read;
        }

        return true;
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _listenSocket?.Close();
        await base.StopAsync(cancellationToken);
    }
}
