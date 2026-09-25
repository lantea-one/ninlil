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
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class implements DNS over TCP (RFC 1035 §4.2.2, RFC 7766): a two-octet big-endian
///     length prefix ahead of every message, and one connection able to carry many queries (RFC
///     7766 §6.2.1 pipelining). Ordinary queries go to <see cref="DnsQueryProcessingPipeline" />,
///     as in <see cref="UdpDnsListener" />. An AXFR or IXFR query is answered here through
///     <see cref="IZoneTransferService" />, since the pipeline produces one response per request
///     and a transfer is a sequence of messages. Transfers therefore skip the pipeline's checks
///     (rate limiting, filtering, the query log) and rely on the transfer service's transport and
///     TSIG checks.
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">
///     The listen addresses, port and connection limits, read through
///     <see cref="IOptionsMonitor{TOptions}" /> each time <see cref="ExecuteAsync" /> starts, so
///     restarting this listener (the <c>plain-tcp</c> managed service) picks up a change without
///     restarting the process.
/// </param>
/// <param name="logger">The logger this listener records connection-level failures to.</param>
public sealed class TcpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PlainProtocolOptions> options,
    ILogger<TcpDnsListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This field contains the listening sockets, one per listen address (or a single
    ///     dual-stack one when none are configured). <see cref="StopAsync" /> closes them, which
    ///     unblocks each accept loop's pending <see cref="Socket.AcceptAsync(CancellationToken)" />
    ///     call.
    /// </summary>
    private ListenSocketSet _listenSockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This field contains the settings read when <see cref="ExecuteAsync" /> last started, so
    ///     every connection uses the same idle timeout until the next restart.
    /// </summary>
    private PlainProtocolOptions _settings;

    /// <summary>
    ///     This field limits how many connections are served at once, per
    ///     <see cref="PlainProtocolOptions.MaxConcurrentTcpConnections" />. It is shared by every
    ///     listen address, so the limit is per transport, not per address.
    /// </summary>
    private SemaphoreSlim _connectionLimiter;

    /// <summary>
    ///     This property reports whether at least one of this listener's sockets is bound and
    ///     accepting connections. <c>TransportListenersHealthCheck</c> reads it, since being
    ///     registered or configured does not mean a transport is running.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)", for the health check.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _listenSockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _settings = options.CurrentValue;
        _connectionLimiter = new SemaphoreSlim(_settings.MaxConcurrentTcpConnections);

        _listenSockets = ListenSocketSet.Bind(
            "Plain DNS (TCP)",
            _settings.ListenAddresses,
            _settings.Port,
            ProtocolType.Tcp,
            logger
        );

        if (_listenSockets.Sockets.Count == 0)
            return;

        IsListening = true;

        await Task.WhenAll(
            _listenSockets.Sockets.Select(socket => AcceptLoopAsync(socket, stoppingToken))
        );
    }

    /// <summary>
    ///     This method accepts connections on one listening socket until shutdown; each listen
    ///     address gets its own loop.
    /// </summary>
    private async Task AcceptLoopAsync(Socket listenSocket, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Socket connection;

            try
            {
                connection = await listenSocket.AcceptAsync(stoppingToken);
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
                // Cancelling a pending AcceptAsync (StopAsync closing the listening socket, or the
                // token) can surface as a SocketException rather than OperationCanceledException
                // or ObjectDisposedException; this has been seen during a graceful shutdown. If we
                // were asked to stop, that is what this is; otherwise keep serving rather than
                // stopping the listener over one failed accept.
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                // Too many connections are open; close this one at once rather than let it queue.
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method serves one accepted connection: it reads each length-prefixed query,
    ///     answers it, and closes the connection when <see cref="PlainProtocolOptions.TcpIdleTimeout" />
    ///     passes, the client disconnects, or a zone transfer on it completes.
    ///     <para>
    ///         Closing after a transfer is required, although RFC 7766 would allow further pipelined
    ///         queries: the pull client in
    ///         <see cref="ZoneTransferPullOverStream.ReadResponsesAsync" /> (used by
    ///         <c>TcpZoneTransferClient</c> for secondary zones) treats the connection closing as
    ///         the end of the transfer, so a connection left open would make it wait until its
    ///         read timeout. An ordinary query leaves the connection open for pipelining.
    ///     </para>
    ///     <para>
    ///         The timeout is restarted for each message and also covers handling it, so a zone
    ///         transfer that takes longer than <see cref="PlainProtocolOptions.TcpIdleTimeout" /> is
    ///         cancelled.
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
                idleCts.CancelAfter(_settings.TcpIdleTimeout);

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
    ///     This method decodes one TCP-framed message and routes it: an AXFR or IXFR query is
    ///     answered here, as a sequence of messages, through <see cref="IZoneTransferService" />;
    ///     every other query is enqueued onto <see cref="DnsQueryProcessingPipeline" />, like a UDP
    ///     query. A message that does not decode is logged and dropped.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the connection just served a zone transfer and should be
    ///     closed (see <see cref="HandleConnectionAsync" /> for why); <see langword="false" /> for
    ///     any other message, which leaves the connection open.
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
            // A malformed message has no reliable ID to answer with, so it is not answered, as in
            // DnsQueryProcessingPipeline.
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
    ///     This method streams an AXFR or IXFR response back, one length-prefixed message at a
    ///     time, in the order <see cref="IZoneTransferService" /> yields them.
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
    ///     This method writes one length-prefixed message to a connection, one writer at a time:
    ///     <see cref="DnsQueryProcessingPipeline" /> may finish two pipelined queries on the same
    ///     connection at once, and interleaved writes would corrupt the framing of both.
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
    ///     Thrown when the connection closes partway through a read that has started: a truncated
    ///     stream rather than an ordinary disconnect between messages.
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
        _listenSockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
