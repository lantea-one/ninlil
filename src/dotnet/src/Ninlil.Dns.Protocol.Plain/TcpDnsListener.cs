using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
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
///     and a transfer is a sequence of messages. <see cref="ZoneTransferRequestRunner" /> gives
///     transfers the pipeline's rate limiting and query log entry; the transfer service's
///     transport, ACL and TSIG checks stay in <see cref="IZoneTransferService" />, and filtering
///     does not apply.
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
/// <param name="rateLimiter">
///     The rate limiter shared with the query pipeline, applied to zone transfers before the
///     transfer service runs (see <see cref="ZoneTransferRequestRunner" />).
///     <see langword="null" /> skips rate limiting.
/// </param>
/// <param name="eventPublisher">
///     The publisher the answered event of each zone transfer goes to, so transfers reach the
///     query log. <see langword="null" /> publishes nothing.
/// </param>
public sealed class TcpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PlainProtocolOptions> options,
    ILogger<TcpDnsListener> logger,
    IDnsRateLimiter rateLimiter = null,
    IDnsServerEventPublisher eventPublisher = null
) : BackgroundService
{
    /// <summary>
    ///     This field runs zone transfers, which this listener answers itself, through the shared
    ///     rate limit and query-log steps.
    /// </summary>
    private readonly ZoneTransferRequestRunner _transferRunner = new(
        scopeFactory,
        rateLimiter,
        eventPublisher
    );

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
    ///     This method serves one accepted connection through a
    ///     <see cref="FramedDnsStreamSession" />, which reads each length-prefixed query and
    ///     closes the connection when <see cref="PlainProtocolOptions.TcpIdleTimeout" /> passes
    ///     with no complete message, the client disconnects, a write to the client misses that
    ///     same timeout, or a zone transfer on it completes. The session owns the per-connection
    ///     limits (16 queries in flight, one writer task); a query still outstanding when the
    ///     connection goes idle is answered first.
    ///     <para>
    ///         The connection closes after a transfer: the session serves one transfer per
    ///         connection. Pull clients (<c>TcpZoneTransferClient</c> for secondary zones) do not
    ///         depend on the close, because a transfer ends at its closing SOA (RFC 5936 §2.2). An
    ///         ordinary query leaves the connection open for pipelining.
    ///     </para>
    /// </summary>
    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken)
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;

        try
        {
            using NetworkStream stream = new(connection, ownsSocket: false);
            FramedDnsStreamSession session = new(
                stream,
                remoteEndpoint.Address,
                _settings.TcpIdleTimeout,
                logger,
                stoppingToken
            );

            await session.RunAsync(
                (messageBytes, _, sessionToken) =>
                    HandleMessageAsync(messageBytes, remoteEndpoint.Address, session, sessionToken)
            );
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
    ///     query, with the session's reply and drop callbacks. A message that does not decode is
    ///     logged and dropped.
    /// </summary>
    /// <returns>
    ///     <see cref="FrameOutcomeEnum.CloseAfterDrain" /> if the connection just served a zone
    ///     transfer (see <see cref="HandleConnectionAsync" /> for why it closes),
    ///     <see cref="FrameOutcomeEnum.Enqueued" /> for a query handed to the pipeline, and
    ///     <see cref="FrameOutcomeEnum.Handled" /> for a message that was discarded.
    /// </returns>
    private async Task<FrameOutcomeEnum> HandleMessageAsync(
        byte[] messageBytes,
        IPAddress remoteAddress,
        FramedDnsStreamSession session,
        CancellationToken sessionToken
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
            // DnsQueryProcessingPipeline. Debug level: a client can send these as fast as it likes.
            logger.LogDebug(
                exception,
                "Discarding a malformed TCP DNS message from {RemoteAddress}.",
                remoteAddress
            );
            return FrameOutcomeEnum.Handled;
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
                session
            );
            return FrameOutcomeEnum.CloseAfterDrain;
        }

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                messageBytes,
                DnsTransportProtocolEnum.Tcp,
                remoteAddress,
                session.CreateResponder()
            )
            {
                OnDropped = session.CreateDropNotifier(),

                // A frame carries at most 65 535 octets; a larger reply is dropped, never cut.
                MaxResponseOctets = ushort.MaxValue,
            },
            sessionToken
        );

        return FrameOutcomeEnum.Enqueued;
    }

    /// <summary>
    ///     This method streams an AXFR or IXFR response back, one length-prefixed message at a
    ///     time, in the order <see cref="IZoneTransferService" /> yields them. The messages go
    ///     through <see cref="FramedDnsStreamSession.SendAsync" />, so a secondary that stops
    ///     reading is cut by the session's write deadline, and the transfer is limited to
    ///     <see cref="FramedDnsStreamSession.MaxTransferDuration" />.
    /// </summary>
    private async Task HandleZoneTransferAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsResourceRecordTypeEnum questionType,
        FramedDnsStreamSession session
    )
    {
        // A transfer is bounded by its own two-hour limit, not by the read-idle timeout.
        using CancellationTokenSource transferCts = session.CreateTransferTokenSource(
            FramedDnsStreamSession.MaxTransferDuration
        );
        CancellationToken transferToken = transferCts.Token;

        // A rate-limited transfer writes nothing; the caller closes the connection after it.
        await _transferRunner.RunAsync(
            request,
            rawMessage,
            tsigRecordStartOffset,
            remoteAddress,
            DnsTransportProtocolEnum.Tcp,
            questionType,
            (messageBytes, token) => session.SendAsync(messageBytes, token),
            transferToken
        );
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _listenSockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
