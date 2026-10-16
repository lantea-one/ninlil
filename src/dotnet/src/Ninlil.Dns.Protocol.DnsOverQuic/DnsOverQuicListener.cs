using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic;

/// <summary>
///     This class implements DNS over QUIC (RFC 9250): one bidirectional stream per query, never
///     reused, carrying a two-octet length-prefixed DNS message each way (RFC 9250 §4.2). TLS 1.3
///     is part of the QUIC handshake, unlike the separate TCP and TLS handshakes of DNS over TLS.
///     Ordinary queries go to <see cref="DnsQueryProcessingPipeline" /> (see that class).
///     <para>
///         AXFR and IXFR queries are answered here through <see cref="IZoneTransferService" />
///         (see <see cref="HandleZoneTransferAsync" />), with every response message written to
///         the query's stream, as <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c> does
///         for XFR-over-TLS.
///     </para>
///     <para>
///         Every query must carry message ID 0 (RFC 9250 §4.2.1). A query with another ID,
///         including an AXFR or IXFR, closes its connection with <c>DOQ_PROTOCOL_ERROR</c>
///         (RFC 9250 §4.3.3) and gets no reply.
///     </para>
///     <para>
///         0-RTT is not enabled: no TLS session resumption is configured on
///         <see cref="SslServerAuthenticationOptions" />, which 0-RTT would need. RFC 9250
///         discusses the replay risk it brings.
///     </para>
///     <para>
///         Connections are limited to <see cref="DnsOverQuicOptions.MaxConcurrentConnections" />
///         (one over it is closed with <c>DOQ_EXCESSIVE_LOAD</c>) and streams per connection to
///         <see cref="DnsOverQuicOptions.MaxConcurrentBidirectionalStreams" />. Every stream has
///         a read deadline and a write deadline of <see cref="DnsOverQuicOptions.IdleTimeout" />,
///         and a stream task, not a pipeline worker, writes the reply, so a client that stops
///         reading cannot stall the pipeline.
///     </para>
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">
///     The listen addresses, port, stream limits and certificate, read through
///     <see cref="IOptionsMonitor{TOptions}" /> so a changed <c>Enable</c> is seen when the
///     listener is started again (see the <c>options</c> parameter of
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>).
/// </param>
/// <param name="logger">The logger this listener records startup failures and connection-level failures to.</param>
/// <param name="rateLimiter">
///     The rate limiter shared with the query pipeline, applied to zone transfers before the
///     transfer service runs (see <see cref="ZoneTransferRequestRunner" />).
///     <see langword="null" /> skips rate limiting.
/// </param>
/// <param name="eventPublisher">
///     The publisher the answered event of each zone transfer goes to, so transfers reach the
///     query log. <see langword="null" /> publishes nothing.
/// </param>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class DnsOverQuicListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsOverQuicOptions> options,
    ILogger<DnsOverQuicListener> logger,
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
    ///     This field contains the ALPN token RFC 9250 registers for DNS-over-QUIC ("doq").
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <summary>
    ///     This field contains the provider of the server certificate presented to every client.
    ///     It re-checks the certificate on disk periodically, so one an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces is picked up for new connections without a
    ///     restart.
    /// </summary>
    private ReloadableTlsCertificateProvider _certificateProvider;

    /// <summary>
    ///     This property reports whether at least one <see cref="QuicListener" /> is bound and
    ///     accepting connections. <c>TransportListenersHealthCheck</c> reads it, since being
    ///     registered or configured does not mean a transport is running.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)", for the health check.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _failedListenAddresses;

    /// <summary>
    ///     This constant contains the name this transport uses in log messages.
    /// </summary>
    private const string TransportName = "DNS-over-QUIC";

    /// <summary>
    ///     This constant contains the DoQ application error code for a protocol violation, here a
    ///     message that does not decode (RFC 9250 §8.4, <c>DOQ_PROTOCOL_ERROR</c>).
    /// </summary>
    private const long DoqProtocolError = 0x2;

    /// <summary>
    ///     This constant contains the DoQ application error code for a stream the server gives
    ///     up on: a missed deadline or a dropped query (RFC 9250 §8.4,
    ///     <c>DOQ_REQUEST_CANCELLED</c>).
    /// </summary>
    private const long DoqRequestCancelled = 0x3;

    /// <summary>
    ///     This constant contains the DoQ application error code for refusing work because the
    ///     server is loaded: a connection over the limit, or a rate-limited query (RFC 9250
    ///     §8.4, <c>DOQ_EXCESSIVE_LOAD</c>).
    /// </summary>
    private const long DoqExcessiveLoad = 0x4;

    /// <summary>
    ///     This field counts the connections being served, across every listen address.
    /// </summary>
    private int _activeConnections;

    /// <summary>
    ///     This field contains the connection limit, read from
    ///     <see cref="DnsOverQuicOptions.MaxConcurrentConnections" /> when the listener starts.
    /// </summary>
    private int _maxConnections = int.MaxValue;

    /// <summary>
    ///     This field backs <see cref="FailedListenAddresses" />, replaced on every start.
    /// </summary>
    private IReadOnlyList<string> _failedListenAddresses = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        if (!QuicListener.IsSupported)
        {
            // QUIC needs libmsquic (and, on Linux, OpenSSL) installed on the host; neither ships
            // with .NET. Logging an error here, without stopping the rest of the host, is clearer
            // than a low-level QuicException later.
            logger.LogError(
                "DNS-over-QUIC is enabled, but QUIC is not supported on this platform -- libmsquic (and, on Linux, OpenSSL) must be installed. This transport will not start; every other part of this server is unaffected."
            );
            return;
        }

        _certificateProvider = new ReloadableTlsCertificateProvider(
            options.CurrentValue.Certificate,
            logger,
            options.CurrentValue.Certificate.CertificateReloadPollInterval
        );

        DnsOverQuicOptions current = options.CurrentValue;
        _maxConnections = current.MaxConcurrentConnections;
        Volatile.Write(ref _activeConnections, 0);
        bool explicitAddresses = ListenAddressList.Split(current.ListenAddresses).Count > 0;

        IReadOnlyList<IPEndPoint> endpoints = ListenSocketSet.ResolveEndpoints(
            TransportName,
            current.ListenAddresses,
            current.Port,
            logger,
            out List<string> failedAddresses
        );
        List<QuicListener> listeners = [];

        try
        {
            foreach (IPEndPoint endpoint in endpoints)
            {
                QuicListener listener = await TryListenAsync(
                    endpoint,
                    explicitAddresses,
                    failedAddresses,
                    stoppingToken
                );

                if (listener is not null)
                    listeners.Add(listener);
            }

            _failedListenAddresses = failedAddresses;

            if (explicitAddresses)
                ListenSocketSet.ReportNothingBound(TransportName, listeners.Count, logger);

            if (listeners.Count == 0)
                return;

            IsListening = true;

            await Task.WhenAll(
                listeners.Select(listener => AcceptLoopAsync(listener, stoppingToken))
            );
        }
        finally
        {
            IsListening = false;

            foreach (QuicListener listener in listeners)
                await listener.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method starts a QUIC listener on one endpoint. With no listen addresses configured
    ///     (<paramref name="explicitAddress" /> false) a failure throws. For a configured address,
    ///     a failure is logged, recorded in
    ///     <paramref name="failedAddresses" />, and <see langword="null" /> is returned so the
    ///     other addresses keep serving. An address this host doesn't have is refused up front:
    ///     MsQuic binds a wildcard socket underneath and filters by address itself, so it would
    ///     otherwise "listen" there successfully and never answer anything.
    /// </summary>
    private async Task<QuicListener> TryListenAsync(
        IPEndPoint endpoint,
        bool explicitAddress,
        List<string> failedAddresses,
        CancellationToken stoppingToken
    )
    {
        QuicListenerOptions listenerOptions = BuildListenerOptions(endpoint);

        if (!explicitAddress)
            return await QuicListener.ListenAsync(listenerOptions, stoppingToken);

        if (!ListenSocketSet.IsAssignedToThisHost(endpoint.Address))
        {
            failedAddresses.Add($"{endpoint.Address} (not assigned to any network interface)");
            logger.LogError(
                "{Transport} could not listen on {Address} port {Port}: the address is not assigned to any network interface on this host. Its other listen addresses keep serving.",
                TransportName,
                endpoint.Address,
                endpoint.Port
            );
            return null;
        }

        try
        {
            return await QuicListener.ListenAsync(listenerOptions, stoppingToken);
        }
        catch (Exception exception)
            when (exception is QuicException or SocketException or ArgumentException)
        {
            failedAddresses.Add($"{endpoint.Address} ({exception.Message})");
            logger.LogError(
                exception,
                "{Transport} could not listen on {Address} port {Port}: {Reason}. Its other listen addresses keep serving.",
                TransportName,
                endpoint.Address,
                endpoint.Port,
                exception.Message
            );
            return null;
        }
    }

    /// <summary>
    ///     This method builds the QUIC listener settings for one endpoint; they are the same for
    ///     every listen address apart from the endpoint. The per-connection settings are read
    ///     from the options for each new connection.
    /// </summary>
    private QuicListenerOptions BuildListenerOptions(IPEndPoint endpoint) =>
        new()
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = [DoqApplicationProtocol],
            ConnectionOptionsCallback = (_, _, _) =>
                ValueTask.FromResult(
                    new QuicServerConnectionOptions
                    {
                        DefaultCloseErrorCode = 0,
                        DefaultStreamErrorCode = 0,
                        IdleTimeout = options.CurrentValue.IdleTimeout,
                        MaxInboundBidirectionalStreams = options
                            .CurrentValue
                            .MaxConcurrentBidirectionalStreams,
                        MaxInboundUnidirectionalStreams = 0,
                        // TlsCertificateOptions.AllowedProtocols is not applied: QUIC always
                        // uses TLS 1.3 (RFC 9001), so restricting EnabledSslProtocols adds
                        // nothing, and it has not been checked that QUIC's handshake accepts it.
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [DoqApplicationProtocol],
                            ServerCertificate = _certificateProvider.Current,
                        },
                    }
                ),
        };

    /// <summary>
    ///     This method accepts connections on one QUIC listener until shutdown; each listen
    ///     address gets its own loop.
    /// </summary>
    private Task AcceptLoopAsync(QuicListener listener, CancellationToken stoppingToken) =>
        AcceptLoopAsync(listener.AcceptConnectionAsync, stoppingToken);

    /// <summary>
    ///     This method is the accept loop itself, taking the accept call as a delegate so a test
    ///     can make it fail without a failing QUIC peer.
    /// </summary>
    internal async Task AcceptLoopAsync(
        Func<CancellationToken, ValueTask<QuicConnection>> acceptAsync,
        CancellationToken stoppingToken
    )
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QuicConnection connection;

            try
            {
                connection = await acceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (exception is QuicException or AuthenticationException)
            {
                // One peer's failed handshake (for example a certificate or protocol error)
                // must end only that connection. Ending the loop would stop this listener
                // address for everyone.
                logger.LogDebug(
                    exception,
                    "A DNS-over-QUIC connection failed before it was accepted."
                );
                continue;
            }

            if (Interlocked.Increment(ref _activeConnections) > _maxConnections)
            {
                Interlocked.Decrement(ref _activeConnections);
                _ = RejectConnectionAsync(connection);
                continue;
            }

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method serves one accepted QUIC connection, handling every stream the peer opens
    ///     concurrently; each stream is an independent query (RFC 9250 §4.2). Streams are
    ///     separate, so unlike DNS over TLS no per-connection write lock is needed. The
    ///     connection holds one slot of <see cref="DnsOverQuicOptions.MaxConcurrentConnections" />
    ///     until it ends, and the token passed to its streams is cancelled then, so a stream
    ///     waiting for a reply the client can no longer receive stops waiting.
    /// </summary>
    private async Task HandleConnectionAsync(
        QuicConnection connection,
        CancellationToken stoppingToken
    )
    {
        TimeSpan idleTimeout = options.CurrentValue.IdleTimeout;
        CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );

        try
        {
            while (true)
            {
                QuicStream stream;

                try
                {
                    stream = await connection.AcceptInboundStreamAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The connection closed (peer disconnect, idle timeout, or a QuicException
                    // from an abrupt reset), so there is nothing left to accept.
                    break;
                }

                _ = HandleStreamAsync(connection, stream, idleTimeout, connectionCts.Token);
            }
        }
        finally
        {
            await connectionCts.CancelAsync();
            connectionCts.Dispose();
            await connection.DisposeAsync();
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    /// <summary>
    ///     This method refuses a connection that arrived over
    ///     <see cref="DnsOverQuicOptions.MaxConcurrentConnections" />, closing it with
    ///     <c>DOQ_EXCESSIVE_LOAD</c> (RFC 9250 §8.4) so a well-behaved client backs off. It runs
    ///     off the accept loop, which must not wait for a slow peer.
    /// </summary>
    private async Task RejectConnectionAsync(QuicConnection connection)
    {
        try
        {
            using CancellationTokenSource closeTimeout = new(TimeSpan.FromSeconds(5));
            await connection.CloseAsync(DoqExcessiveLoad, closeTimeout.Token);
        }
        catch (Exception exception)
            when (exception
                    is QuicException
                        or OperationCanceledException
                        or ObjectDisposedException
            )
        {
            logger.LogDebug(exception, "Closing an over-limit DNS-over-QUIC connection failed.");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method answers the query on one stream: it reads the length-prefixed message
    ///     within <paramref name="idleTimeout" /> (the client then closes its side of the stream,
    ///     so nothing more needs reading), routes a zone transfer to
    ///     <see cref="HandleZoneTransferAsync" /> and any other query to the pipeline. A pipeline
    ///     worker never writes to the stream: the stream's own task waits for the reply, then
    ///     writes it framed the same way and closes the stream's write side (RFC 9250 §4.2),
    ///     within <paramref name="idleTimeout" />. A stream that misses a deadline, or whose
    ///     query the pipeline drops, is reset with a DoQ error code (RFC 9250 §8.4) instead of
    ///     being left open. A message that does not decode is logged at Debug and the stream
    ///     reset. A message whose ID is not 0 (RFC 9250 §4.2.1) is a protocol error: the whole
    ///     connection is closed with <c>DOQ_PROTOCOL_ERROR</c> and nothing is answered (RFC 9250
    ///     §4.3.3). The source address is the QUIC connection's remote address.
    /// </summary>
    private async Task HandleStreamAsync(
        QuicConnection connection,
        QuicStream stream,
        TimeSpan idleTimeout,
        CancellationToken connectionToken
    )
    {
        try
        {
            byte[] messageBuffer;

            using (
                CancellationTokenSource readCts = LinkedWithTimeout(connectionToken, idleTimeout)
            )
            {
                messageBuffer = await ReadQueryAsync(stream, readCts.Token);
            }

            if (messageBuffer is null)
                return;

            DnsMessage request;
            int tsigRecordStartOffset;

            try
            {
                request = DnsMessageReader.Read(messageBuffer, out tsigRecordStartOffset);
            }
            catch (FormatException exception)
            {
                logger.LogDebug(
                    exception,
                    "Discarding a malformed DNS-over-QUIC message from {RemoteAddress}.",
                    connection.RemoteEndPoint.Address
                );
                AbortQuietly(stream, DoqProtocolError);
                return;
            }

            // RFC 9250 §4.2.1 requires ID 0 on every DoQ message, and §4.3.3 makes a non-zero ID
            // a fatal protocol error for the connection. This covers zone transfers too.
            if (request.Id != 0)
            {
                logger.LogDebug(
                    "Closing a DNS-over-QUIC connection from {RemoteAddress}: the query carried message ID {MessageId}, not 0.",
                    connection.RemoteEndPoint.Address,
                    request.Id
                );
                await CloseConnectionQuietlyAsync(connection, DoqProtocolError);
                return;
            }

            DnsResourceRecordTypeEnum? questionType =
                request.Questions.Count > 0 ? request.Questions[0].Type : null;

            if (questionType is DnsResourceRecordTypeEnum.Axfr or DnsResourceRecordTypeEnum.Ixfr)
            {
                await HandleZoneTransferAsync(
                    request,
                    messageBuffer,
                    tsigRecordStartOffset,
                    connection.RemoteEndPoint.Address,
                    questionType.Value,
                    stream,
                    idleTimeout,
                    connectionToken
                );
                return;
            }

            TaskCompletionSource<QueryOutcome> outcome = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    messageBuffer,
                    DnsTransportProtocolEnum.Quic,
                    connection.RemoteEndPoint.Address,
                    (responseBytes, _) =>
                    {
                        // Copied, so the pipeline may reuse its buffer; the stream task writes
                        // it later.
                        outcome.TrySetResult(new QueryOutcome(responseBytes.ToArray(), default));
                        return ValueTask.CompletedTask;
                    }
                )
                {
                    OnDropped = reason => outcome.TrySetResult(new QueryOutcome(null, reason)),
                    MaxResponseOctets = ushort.MaxValue,
                },
                connectionToken
            );

            QueryOutcome result = await outcome.Task.WaitAsync(connectionToken);

            if (result.Response is null)
            {
                AbortQuietly(
                    stream,
                    result.DropReason
                        is DnsQueryDropReasonEnum.RateLimited
                            or DnsQueryDropReasonEnum.SubnetRateLimited
                        ? DoqExcessiveLoad
                        : DoqRequestCancelled
                );
                return;
            }

            await WriteAsync(
                stream,
                Frame(result.Response),
                completeWrites: true,
                idleTimeout,
                connectionToken
            );
        }
        catch (OperationCanceledException)
        {
            // A read or write deadline passed, or the connection or host is closing.
            logger.LogDebug(
                "DNS-over-QUIC stream from {Endpoint} was reset: a deadline passed or the connection closed.",
                connection.RemoteEndPoint
            );
            AbortQuietly(stream, DoqRequestCancelled);
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "DNS-over-QUIC stream from {Endpoint} ended abnormally.",
                connection.RemoteEndPoint
            );
            AbortQuietly(stream, DoqRequestCancelled);
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method answers an AXFR or IXFR query on its stream with the sequence of messages
    ///     from <see cref="IZoneTransferService" />, the DNS-over-QUIC counterpart of the transfer
    ///     handling in <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c> and
    ///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>. Like those, it runs under
    ///     <see cref="FramedDnsStreamSession.MaxTransferDuration" />, not the read-idle timeout,
    ///     because a transfer is long by nature; a secondary that stops reading is cut by the
    ///     per-write deadline instead.
    /// </summary>
    private async Task HandleZoneTransferAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsResourceRecordTypeEnum questionType,
        QuicStream stream,
        TimeSpan writeDeadline,
        CancellationToken connectionToken
    )
    {
        using CancellationTokenSource transferCts = LinkedWithTimeout(
            connectionToken,
            FramedDnsStreamSession.MaxTransferDuration
        );
        CancellationToken transferToken = transferCts.Token;

        // Every response message goes on this stream, since RFC 9250 §4.2 gives each query
        // one stream and a transfer is the answer to one query. Only the last message closes
        // the stream's write side, so each message is held back one step until the next
        // arrives.
        byte[] pendingFrame = null;

        bool served = await _transferRunner.RunAsync(
            request,
            rawMessage,
            tsigRecordStartOffset,
            remoteAddress,
            DnsTransportProtocolEnum.Quic,
            questionType,
            async (messageBytes, token) =>
            {
                if (pendingFrame is not null)
                    await WriteAsync(
                        stream,
                        pendingFrame,
                        completeWrites: false,
                        writeDeadline,
                        token
                    );

                pendingFrame = Frame(messageBytes);
            },
            transferToken
        );

        if (!served)
        {
            // Rate limited: reset the stream without a reply, as for a throttled ordinary query.
            AbortQuietly(stream, DoqExcessiveLoad);
            return;
        }

        if (pendingFrame is not null)
            await WriteAsync(
                stream,
                pendingFrame,
                completeWrites: true,
                writeDeadline,
                transferToken
            );
    }

    /// <summary>
    ///     This method returns a linked token source that is cancelled with
    ///     <paramref name="parent" /> or after <paramref name="timeout" />. The caller disposes it.
    /// </summary>
    private static CancellationTokenSource LinkedWithTimeout(
        CancellationToken parent,
        TimeSpan timeout
    )
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        source.CancelAfter(timeout);
        return source;
    }

    /// <summary>
    ///     This method prefixes a DNS message with its two-octet length (RFC 9250 §4.2).
    /// </summary>
    private static byte[] Frame(ReadOnlySpan<byte> messageBytes)
    {
        byte[] framed = new byte[2 + messageBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)messageBytes.Length);
        messageBytes.CopyTo(framed.AsSpan(2));
        return framed;
    }

    /// <summary>
    ///     This method writes one framed message under its own <paramref name="writeDeadline" />.
    ///     QUIC flow control makes a write wait for a client that does not read, so without a
    ///     deadline such a client would hold the stream open indefinitely.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when the deadline passes or <paramref name="token" /> is cancelled.
    /// </exception>
    private static async ValueTask WriteAsync(
        QuicStream stream,
        ReadOnlyMemory<byte> framed,
        bool completeWrites,
        TimeSpan writeDeadline,
        CancellationToken token
    )
    {
        using CancellationTokenSource writeCts = LinkedWithTimeout(token, writeDeadline);
        await stream.WriteAsync(framed, completeWrites, writeCts.Token);
    }

    /// <summary>
    ///     This method resets both directions of a stream with a DoQ application error code.
    ///     The stream may already be closed by the peer, which is ignored.
    /// </summary>
    private static void AbortQuietly(QuicStream stream, long errorCode)
    {
        try
        {
            stream.Abort(QuicAbortDirection.Both, errorCode);
        }
        catch (Exception exception)
            when (exception is QuicException or ObjectDisposedException or InvalidOperationException
            )
        {
            // Already closed; nothing left to reset.
        }
    }

    /// <summary>
    ///     This method closes a connection with a DoQ error code (RFC 9250 §4.3.3) and swallows
    ///     the errors a peer that is already gone causes. The close is bounded, so a stalled peer
    ///     cannot hold the stream task.
    /// </summary>
    private static async Task CloseConnectionQuietlyAsync(QuicConnection connection, long errorCode)
    {
        try
        {
            using CancellationTokenSource closeTimeout = new(TimeSpan.FromSeconds(5));
            await connection.CloseAsync(errorCode, closeTimeout.Token);
        }
        catch (Exception exception)
            when (exception
                    is QuicException
                        or OperationCanceledException
                        or ObjectDisposedException
            )
        {
            // Already closed; nothing left to close.
        }
    }

    /// <summary>
    ///     This method reads a length-prefixed query from a stream.
    /// </summary>
    /// <returns>The message bytes, or <see langword="null" /> if the stream ended with no data.</returns>
    private static async ValueTask<byte[]> ReadQueryAsync(
        QuicStream stream,
        CancellationToken token
    )
    {
        byte[] lengthPrefix = new byte[2];

        if (!await ReadExactAsync(stream, lengthPrefix, token))
            return null;

        byte[] messageBuffer = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];

        if (!await ReadExactAsync(stream, messageBuffer, token))
            throw new IOException("The stream ended before the DNS message arrived.");

        return messageBuffer;
    }

    /// <summary>
    ///     This record carries how a pipeline query ended to the stream task waiting for it:
    ///     with a reply, or (<paramref name="Response" /> null) dropped for the given reason.
    /// </summary>
    private readonly record struct QueryOutcome(byte[] Response, DnsQueryDropReasonEnum DropReason);

    /// <summary>
    ///     This method fills a buffer completely from a stream, or reports a clean end-of-stream.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> once <paramref name="buffer" /> is completely filled, or
    ///     <see langword="false" /> if the stream ended before the first byte of this read
    ///     arrived.
    /// </returns>
    /// <exception cref="IOException">
    ///     Thrown when the stream ends partway through a read that has started.
    /// </exception>
    private static async ValueTask<bool> ReadExactAsync(
        QuicStream stream,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
            {
                if (totalRead == 0)
                    return false;

                throw new IOException("The stream ended partway through a DNS message.");
            }

            totalRead += read;
        }

        return true;
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // ExecuteAsync creates a new ReloadableTlsCertificateProvider (with its polling timer)
        // on every start, as DnsOverTlsListener does. Dispose() runs only at final host
        // shutdown, so disposing only there would leak a timer per restart.
        _certificateProvider?.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _certificateProvider?.Dispose();
        base.Dispose();
    }
}
