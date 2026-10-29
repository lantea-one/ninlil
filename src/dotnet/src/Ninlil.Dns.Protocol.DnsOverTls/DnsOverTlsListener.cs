using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls;

/// <summary>
///     This class implements DNS over TLS (RFC 7858): the two-octet length-prefixed framing of DNS
///     over TCP (RFC 1035 §4.2.2, RFC 7766; RFC 7858 §3.3 reuses it) inside a TLS session, so a
///     network observer can see that a query happened but not what it asked. Ordinary queries go
///     to <see cref="DnsQueryProcessingPipeline" /> (see that class). It also serves RFC 9103
///     XFR-over-TLS: an AXFR or IXFR query is answered here through
///     <see cref="IZoneTransferService" /> (see <see cref="HandleZoneTransferAsync" />), as
///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c> does. TSIG still authenticates the
///     transfer; TLS adds confidentiality.
///     <para>
///         A trusted TCP proxy can pass the client address with a PROXY protocol header (see
///         <see cref="ProxyProtocolOptions" />). Otherwise the client address is the socket peer.
///         On the default dual-stack socket that is IPv4-mapped for IPv4 clients, and
///         <see cref="DnsInboundMessage" /> turns it into plain IPv4.
///     </para>
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">
///     The listen addresses, port, connection limits and certificate, read through
///     <see cref="IOptionsMonitor{TOptions}" /> rather than <see cref="IOptions{TOptions}" /> so
///     that when <c>Ninlil.Web</c>'s transport settings controller changes <c>Enable</c> and
///     starts the listener again, <see cref="ExecuteAsync" /> sees the new value rather than the
///     one from process startup.
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
public sealed class DnsOverTlsListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsOverTlsOptions> options,
    ILogger<DnsOverTlsListener> logger,
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
    ///     This field limits how many connections are served at once, per
    ///     <see cref="DnsOverTlsOptions.MaxConcurrentConnections" />, across all listen addresses.
    /// </summary>
    private SemaphoreSlim _connectionLimiter;

    /// <summary>
    ///     This field contains the provider of the server certificate presented to every client.
    ///     It re-checks the certificate on disk periodically, so one an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces is picked up without a restart.
    /// </summary>
    private ReloadableTlsCertificateProvider _certificateProvider;

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

    /// <summary>
    ///     This field contains how long <see cref="HandleConnectionAsync" /> waits for a complete
    ///     PROXY protocol header from a trusted source before aborting the connection: the
    ///     specification's recommended minimum ("at least 3 seconds to cover a TCP retransmit")
    ///     plus a margin. It is fixed rather than configurable.
    /// </summary>
    private static readonly TimeSpan ProxyProtocolHeaderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This field contains the longest a client may take to complete the TLS handshake. It is
    ///     Kestrel's default <c>HandshakeTimeout</c>; a shorter idle timeout lowers it (see
    ///     <see cref="HandshakeTimeoutFor" />).
    /// </summary>
    private static readonly TimeSpan MaxHandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This field contains the individual addresses trusted to send a PROXY protocol header,
    ///     parsed from <see cref="DnsOverTlsOptions.ProxyProtocol" /> when the listener starts.
    /// </summary>
    private List<IPAddress> _knownProxyProtocolAddresses;

    /// <summary>
    ///     This field contains the CIDR networks trusted to send a PROXY protocol header, parsed
    ///     from <see cref="DnsOverTlsOptions.ProxyProtocol" /> when the listener starts.
    /// </summary>
    private List<IPNetwork> _knownProxyProtocolNetworks;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        _certificateProvider = new ReloadableTlsCertificateProvider(
            options.CurrentValue.Certificate,
            logger,
            options.CurrentValue.Certificate.CertificateReloadPollInterval
        );
        _connectionLimiter = new SemaphoreSlim(options.CurrentValue.MaxConcurrentConnections);

        // Parsed once per start rather than per connection, like the other options read here.
        ProxyProtocolTrustEvaluator.ParseKnownSources(
            options.CurrentValue.ProxyProtocol,
            logger,
            out _knownProxyProtocolAddresses,
            out _knownProxyProtocolNetworks
        );

        _listenSockets = ListenSocketSet.Bind(
            "DNS-over-TLS",
            options.CurrentValue.ListenAddresses,
            options.CurrentValue.Port,
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
    ///     address gets its own loop, all sharing one connection limit.
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
                // A cancelled accept can surface as a SocketException rather than either
                // exception above (see Ninlil.Dns.Protocol.Plain.TcpDnsListener).
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                // Too many connections are open; close this one at once.
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method serves one accepted connection: it reads a PROXY protocol header if the peer
    ///     is trusted to send one (see <see cref="TryReadProxyProtocolHeaderAsync" />), completes the
    ///     TLS handshake within <see cref="HandshakeTimeoutFor" />, then hands the encrypted stream
    ///     to a <see cref="FramedDnsStreamSession" />. The session reads each length-prefixed query
    ///     and closes the connection when <see cref="DnsOverTlsOptions.IdleTimeout" /> passes with
    ///     no complete message, the client disconnects, a write to the client misses that same
    ///     timeout, or a zone transfer on it completes. It owns the per-connection limits (16
    ///     queries in flight, one writer task); a query still outstanding when the connection goes
    ///     idle is answered first. A client that connects and never completes the handshake would
    ///     otherwise hold a connection slot until shutdown, so the handshake has its own deadline.
    /// </summary>
    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken)
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;
        IPAddress remoteAddress = remoteEndpoint.Address;
        // Every write is a complete reply batch that a client is waiting for. With Nagle's
        // algorithm on (it holds a small segment while earlier data is unacknowledged), the next
        // batch of a pipelined connection waits for the client's delayed acknowledgement, which is
        // tens of milliseconds.
        connection.NoDelay = true;
        NetworkStream networkStream = new(connection, ownsSocket: true);
        SslStream sslStream = null;

        try
        {
            // A PROXY protocol header comes before whatever the proxy relays (here, the TLS
            // ClientHello), so it is read off the plaintext stream before SslStream wraps it.
            if (
                !await TryReadProxyProtocolHeaderAsync(
                    networkStream,
                    remoteEndpoint,
                    stoppingToken,
                    updatedRemoteAddress => remoteAddress = updatedRemoteAddress
                )
            )
                return;

            sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            TimeSpan idleTimeout = options.CurrentValue.IdleTimeout;

            using (
                CancellationTokenSource handshakeCts =
                    CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
            )
            {
                handshakeCts.CancelAfter(HandshakeTimeoutFor(idleTimeout));

                try
                {
                    await sslStream.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificateSelectionCallback = (_, _) =>
                                _certificateProvider.Current,
                            ClientCertificateRequired = false,
                            EnabledSslProtocols = options.CurrentValue.Certificate.AllowedProtocols,
                        },
                        handshakeCts.Token
                    );
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogDebug(
                        "Closing a DNS-over-TLS connection from {Endpoint}: the TLS handshake did not finish in time.",
                        remoteEndpoint
                    );
                    return;
                }
            }

            FramedDnsStreamSession session = new(
                sslStream,
                remoteAddress,
                idleTimeout,
                logger,
                stoppingToken
            );

            // A completed zone transfer closes the connection; see
            // Ninlil.Dns.Protocol.Plain.TcpDnsListener.HandleConnectionAsync for why.
            await session.RunAsync(
                (messageBytes, _, sessionToken) =>
                    HandleMessageAsync(messageBytes, remoteAddress, session, sessionToken)
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "DNS-over-TLS connection from {Endpoint} ended abnormally.",
                remoteEndpoint
            );
        }
        finally
        {
            if (sslStream is not null)
                await sslStream.DisposeAsync();
            else
                await networkStream.DisposeAsync();

            _connectionLimiter.Release();
        }
    }

    /// <summary>
    ///     This method gives the time a client has to complete the TLS handshake: the smaller of
    ///     <see cref="MaxHandshakeTimeout" /> and the idle timeout, so the handshake is never
    ///     allowed longer than a silent connection would be.
    /// </summary>
    private static TimeSpan HandshakeTimeoutFor(TimeSpan idleTimeout) =>
        idleTimeout < MaxHandshakeTimeout ? idleTimeout : MaxHandshakeTimeout;

    /// <summary>
    ///     This method decodes one framed message and routes it: an AXFR or IXFR query is answered
    ///     here, as a sequence of messages, through <see cref="IZoneTransferService" />; every other
    ///     query is enqueued onto <see cref="DnsQueryProcessingPipeline" />, as in
    ///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>, with the session's reply and drop
    ///     callbacks. A message that does not decode is logged and dropped.
    /// </summary>
    /// <returns>
    ///     <see cref="FrameOutcomeEnum.CloseAfterDrain" /> if the connection just served a zone
    ///     transfer (see <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener.HandleConnectionAsync</c> for
    ///     why it closes), <see cref="FrameOutcomeEnum.Enqueued" /> for a query handed to the
    ///     pipeline, and <see cref="FrameOutcomeEnum.Handled" /> for a message that was discarded.
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
                "Discarding a malformed DNS-over-TLS message from {RemoteAddress}.",
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
                DnsTransportProtocolEnum.Tls,
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
    ///     This method streams an AXFR or IXFR response back over the TLS session, one
    ///     length-prefixed message at a time, in the order <see cref="IZoneTransferService" /> yields
    ///     them. It is the XFR-over-TLS (RFC 9103) counterpart of the method of the same name in
    ///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>: the messages go through
    ///     <see cref="FramedDnsStreamSession.SendAsync" />, so a secondary that stops reading is cut
    ///     by the session's write deadline, and the transfer is limited to
    ///     <see cref="FramedDnsStreamSession.MaxTransferDuration" /> rather than the idle timeout.
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
            DnsTransportProtocolEnum.Tls,
            questionType,
            (messageBytes, token) => session.SendAsync(messageBytes, token),
            transferToken
        );
    }

    /// <summary>
    ///     This method reads and checks a PROXY protocol header at the start of a connection,
    ///     before the TLS handshake, but only when the feature is on and the peer is trusted to
    ///     send one (<see cref="ProxyProtocolTrustEvaluator.IsKnownSource" />). A connection from
    ///     any other source is never inspected for a header, so a client outside the allowlist
    ///     cannot forge one, as <c>Ninlil.Web.Middleware.ReverseProxyTrustGatingMiddleware</c> does
    ///     for forwarded HTTP headers.
    ///     <para>
    ///         A trusted peer must send a header on every connection, so a missing, malformed or
    ///         late header is not taken to mean "no PROXY protocol here" and let through with the
    ///         peer address. It is treated as a trust failure (the proxy is misconfigured, or the
    ///         connection did not come from it) and the connection is rejected, as the
    ///         specification requires: "any sequence which does not exactly match the protocol must
    ///         be discarded and cause the receiver to abort the connection."
    ///     </para>
    /// </summary>
    /// <param name="networkStream">The connection stream, before TLS.</param>
    /// <param name="remoteEndpoint">The connection's peer endpoint, used for the trust check and logging.</param>
    /// <param name="stoppingToken">The listener's shutdown token.</param>
    /// <param name="applyClaimedAddress">Called with the header's claimed client address, when it carries one.</param>
    /// <returns>
    ///     <see langword="true" /> if the connection should go on to the TLS handshake (whether or
    ///     not a header was expected), or <see langword="false" /> if it must be dropped.
    /// </returns>
    private async Task<bool> TryReadProxyProtocolHeaderAsync(
        NetworkStream networkStream,
        IPEndPoint remoteEndpoint,
        CancellationToken stoppingToken,
        Action<IPAddress> applyClaimedAddress
    )
    {
        if (
            !options.CurrentValue.ProxyProtocol.Enable
            || !ProxyProtocolTrustEvaluator.IsKnownSource(
                remoteEndpoint.Address,
                _knownProxyProtocolAddresses,
                _knownProxyProtocolNetworks
            )
        )
            return true;

        using CancellationTokenSource headerCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );
        headerCts.CancelAfter(ProxyProtocolHeaderTimeout);

        ProxyProtocolParseResult headerResult;

        try
        {
            headerResult = await ProxyProtocolReader.ReadAsync(networkStream, headerCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The header timeout passed without a complete header; treat it as malformed.
            headerResult = ProxyProtocolParseResult.Failed;
        }

        if (!headerResult.IsValid)
        {
            logger.LogWarning(
                "Rejecting a DNS-over-TLS connection from {Endpoint}: this source is trusted to "
                    + "send a PROXY protocol header, but did not send a valid one.",
                remoteEndpoint
            );
            return false;
        }

        if (headerResult.ClaimedSourceAddress is not null)
            applyClaimedAddress(headerResult.ClaimedSourceAddress);

        return true;
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _listenSockets.Close();
        await base.StopAsync(cancellationToken);

        // ExecuteAsync creates a new ReloadableTlsCertificateProvider (with its polling timer)
        // on every start. Dispose() runs only at final host shutdown, so disposing only there
        // would leak a timer per restart. It is safe while a connection is still handshaking:
        // Dispose only stops the timer, and Current still returns the loaded certificate.
        _certificateProvider?.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _certificateProvider?.Dispose();
        base.Dispose();
    }
}
