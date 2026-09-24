using System.Buffers.Binary;
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
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls;

/// <summary>
///     This class implements RFC 7858's DNS-over-TLS transport: the same 2-octet length-
///     prefixed message framing RFC 1035 §4.2.2/RFC 7766 already define for plain DNS-over-TCP
///     (RFC 7858 §3.3 says so explicitly), carried inside a TLS session instead of a bare TCP
///     one, so a network observer can see that a DNS query happened but not what it asked.
///     Also answers RFC 9103 XFR-over-TLS: an AXFR/IXFR query arriving on this same connection is
///     recognized (see <see cref="HandleZoneTransferAsync" />) and answered directly through
///     <see cref="IZoneTransferService" />'s own multi-message sequence, streamed back over this
///     same <see cref="SslStream" />, exactly mirroring how <c>Ninlil.Dns.Protocol.Plain</c>'s own
///     TCP listener special-cases the identical query types -- TSIG remains the actual
///     authentication layer either way, this transport only adds transport confidentiality.
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">
///     This transport's own listen port, connection limits, and certificate -- read via
///     <see cref="IOptionsMonitor{TOptions}" /> rather than a fixed <see cref="IOptions{TOptions}" />
///     snapshot specifically so a REST-driven <c>enable</c> toggle (mid-process, no restart) is
///     genuinely observed the next time <see cref="ExecuteAsync" /> itself runs: a caller that
///     writes a fresh configuration value and then calls this component's own
///     <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync" /> again (exactly what
///     <c>Ninlil.Web</c>'s own transport settings controller does) needs
///     <see cref="ExecuteAsync" />'s very first check to see that new value, not whatever this
///     transport's <c>Enable</c> flag happened to be at process startup.
/// </param>
/// <param name="logger">The logger this listener records connection-level failures to.</param>
public sealed class DnsOverTlsListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsOverTlsOptions> options,
    ILogger<DnsOverTlsListener> logger
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
    ///     <see cref="DnsOverTlsOptions.MaxConcurrentConnections" />.
    /// </summary>
    private SemaphoreSlim _connectionLimiter;

    /// <summary>
    ///     This property contains the server certificate presented to every connecting client --
    ///     re-checked periodically against disk, so a certificate an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces mid-run is picked up without a restart.
    /// </summary>
    private ReloadableTlsCertificateProvider _certificateProvider;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting connections -- read by the <c>/status</c> health-check endpoint, which needs
    ///     a real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property contains how long <see cref="HandleConnectionAsync" /> waits for a
    ///     complete PROXY protocol header, from an already-trusted source, before giving up and
    ///     aborting the connection -- the PROXY protocol specification's own recommended minimum
    ///     ("at least 3 seconds to cover a TCP retransmit"), with a small margin added; not itself
    ///     configurable, matching this project's own precedent of a fixed, undocumented-as-a-
    ///     setting timeout for a narrow, defensive read (compare
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own fixed regex match
    ///     timeout).
    /// </summary>
    private static readonly TimeSpan ProxyProtocolHeaderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This property contains every individually-listed address this listener trusts to open
    ///     a connection carrying a PROXY protocol header, parsed once from
    ///     <see cref="DnsOverTlsOptions.ProxyProtocol" /> at startup.
    /// </summary>
    private List<IPAddress> _knownProxyProtocolAddresses;

    /// <summary>
    ///     This property contains every CIDR network this listener trusts to open a connection
    ///     carrying a PROXY protocol header, parsed once from
    ///     <see cref="DnsOverTlsOptions.ProxyProtocol" /> at startup.
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

        // Parsed once, up front, rather than re-parsed on every single connection -- this
        // listener re-reads every option fresh from IOptionsMonitor<T> only when ExecuteAsync
        // itself (re)starts, the same restart granularity every other option this method reads
        // only once here already has.
        ProxyProtocolTrustEvaluator.ParseKnownSources(
            options.CurrentValue.ProxyProtocol,
            logger,
            out _knownProxyProtocolAddresses,
            out _knownProxyProtocolNetworks
        );

        _listenSocket = new Socket(
            AddressFamily.InterNetworkV6,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        _listenSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.CurrentValue.Port));
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
                // Mirrors Ninlil.Dns.Protocol.Plain.TcpDnsListener's own observed shutdown
                // behavior: a cancelled accept surfaces here as a SocketException on this
                // platform/runtime, not as either exception above.
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                // Too many connections already open; reject this one immediately.
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method owns a single accepted connection for its entire lifetime: optionally
    ///     reading a PROXY protocol header off it (see <see cref="TryReadProxyProtocolHeaderAsync" />),
    ///     completing its TLS handshake, then reading every length-prefixed query it sends and
    ///     answering each in turn, until the idle timeout elapses or the client disconnects.
    /// </summary>
    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken)
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;
        IPAddress remoteAddress = remoteEndpoint.Address;
        SemaphoreSlim writeLock = new(1, 1);
        NetworkStream networkStream = new(connection, ownsSocket: true);
        SslStream sslStream = null;

        try
        {
            // A PROXY protocol header, when this connection's own peer is trusted to send one,
            // must be read directly off the raw, still-plaintext stream -- it always precedes
            // whatever the proxy is actually relaying (here, the TLS ClientHello itself), so this
            // must happen before SslStream is ever constructed around this same stream.
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

            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, _) => _certificateProvider.Current,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = options.CurrentValue.Certificate.AllowedProtocols,
                },
                stoppingToken
            );

            using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken
            );

            while (true)
            {
                idleCts.CancelAfter(options.CurrentValue.IdleTimeout);

                byte[] lengthPrefix = new byte[2];

                if (!await ReadExactAsync(sslStream, lengthPrefix, idleCts.Token))
                    break;

                ushort messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
                byte[] messageBuffer = new byte[messageLength];

                if (!await ReadExactAsync(sslStream, messageBuffer, idleCts.Token))
                    break;

                bool wasZoneTransfer = await HandleMessageAsync(
                    messageBuffer,
                    remoteAddress,
                    sslStream,
                    writeLock,
                    idleCts.Token
                );

                // A completed zone transfer closes this connection rather than keeping it open
                // for further pipelined queries -- see
                // Ninlil.Dns.Protocol.Plain.TcpDnsListener.HandleConnectionAsync's own doc
                // comment for why this project's own zone-transfer client (and any standards-
                // conforming reference client) has no way to detect a transfer's completion other
                // than the connection actually closing.
                if (wasZoneTransfer)
                    break;
            }
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
    ///     This method decodes a single TLS-framed message and routes it: an AXFR/IXFR query is
    ///     answered directly, as a real multi-message sequence, through
    ///     <see cref="IZoneTransferService" />; every other query is enqueued onto the shared
    ///     <see cref="DnsQueryProcessingPipeline" />, exactly as <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>
    ///     already does for its own plain-TCP connections.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if this connection just finished serving a zone transfer and
    ///     should now be closed -- see <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener
    ///     .HandleConnectionAsync</c>'s own doc comment for why.
    /// </returns>
    private async Task<bool> HandleMessageAsync(
        byte[] messageBytes,
        IPAddress remoteAddress,
        SslStream sslStream,
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
                "Discarding a malformed DNS-over-TLS message from {RemoteAddress}.",
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
                sslStream,
                writeLock,
                stoppingToken
            );
            return true;
        }

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                messageBytes,
                DnsTransportProtocolEnum.Tls,
                remoteAddress,
                (responseBytes, sendToken) =>
                    WriteFramedAsync(sslStream, writeLock, responseBytes, sendToken)
            ),
            stoppingToken
        );

        return false;
    }

    /// <summary>
    ///     This method streams a complete AXFR or IXFR response back over this same
    ///     <see cref="SslStream" />, one length-prefixed message at a time, in the exact order
    ///     <see cref="IZoneTransferService" /> yields them -- the RFC 9103 XFR-over-TLS
    ///     counterpart to <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>'s own identically-shaped
    ///     private method of the same name.
    /// </summary>
    private async Task HandleZoneTransferAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsResourceRecordTypeEnum questionType,
        SslStream sslStream,
        SemaphoreSlim writeLock,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneTransferService transferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsQueryContext context = new(
            request,
            DnsTransportProtocolEnum.Tls,
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
                sslStream,
                writeLock,
                DnsMessageWriter.Write(responseMessage),
                stoppingToken
            );
    }

    /// <summary>
    ///     This method optionally reads and validates a PROXY protocol header off the very start
    ///     of a connection, ahead of the TLS handshake -- but only when this connection's own peer
    ///     is one this listener actually trusts to send one at all
    ///     (<see cref="ProxyProtocolTrustEvaluator.IsKnownSource" />). A connection from any other
    ///     source is never even inspected for a PROXY protocol prefix, so a client outside the
    ///     configured allowlist has no way to forge one, exactly the same anti-spoofing posture
    ///     <c>Ninlil.Web.Middleware.ReverseProxyTrustGatingMiddleware</c> already takes for
    ///     a forwarded HTTP header.
    ///     <para>
    ///         Once a peer <em>is</em> trusted, the PROXY protocol specification requires it to
    ///         always send a header, on every connection, with no ambiguity about whether one is
    ///         present -- so a missing or malformed header from a trusted peer is never treated as
    ///         "this connection just doesn't use the PROXY protocol" and silently allowed through
    ///         on the raw connection's own real address. It is instead a protocol violation this
    ///         method treats as a genuine trust failure (the trusted proxy is misconfigured, or
    ///         this connection did not actually originate from it at all, despite sharing an
    ///         allowlisted address) and rejects outright, per the specification's own explicit
    ///         guidance: "any sequence which does not exactly match the protocol must be discarded
    ///         and cause the receiver to abort the connection."
    ///     </para>
    /// </summary>
    /// <param name="networkStream">The raw, not-yet-TLS-wrapped connection stream.</param>
    /// <param name="remoteEndpoint">The connection's own real peer endpoint, used only for logging.</param>
    /// <param name="stoppingToken">The listener's own shutdown token.</param>
    /// <param name="applyClaimedAddress">Invoked with the header's own claimed client address, when it carries one.</param>
    /// <returns>
    ///     <see langword="true" /> if the connection should proceed to the TLS handshake (whether
    ///     or not a header was ever expected or read), or <see langword="false" /> if it must be
    ///     abandoned immediately.
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
            // The header-read timeout elapsed without a complete header ever arriving -- treated
            // identically to a structurally malformed one, per this method's own doc comment.
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

    /// <summary>
    ///     This method writes a single length-prefixed message to a connection, serialized
    ///     against every other write on that same connection -- necessary because
    ///     <see cref="DnsQueryProcessingPipeline" /> may finish answering two pipelined queries on
    ///     the same connection out of order, and an interleaved write would corrupt the framing
    ///     for both.
    /// </summary>
    private static async ValueTask WriteFramedAsync(
        Stream stream,
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

            await stream.WriteAsync(lengthPrefix, stoppingToken);
            await stream.WriteAsync(messageBytes, stoppingToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    ///     This method fills a buffer completely from a stream, or reports a clean disconnect.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> once <paramref name="buffer" /> is completely filled, or
    ///     <see langword="false" /> if the connection closed before the first byte of this read
    ///     arrived (a clean disconnect between messages, not a truncated one).
    /// </returns>
    /// <exception cref="IOException">
    ///     Thrown when the connection closes partway through an already-started read.
    /// </exception>
    private static async ValueTask<bool> ReadExactAsync(
        Stream stream,
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

        // ExecuteAsync creates a brand-new ReloadableTlsCertificateProvider (and the polling
        // Timer inside it) on every restart; disposing only in this class's own Dispose() --
        // called once, at final host shutdown, not on every mid-process stop -- would leak one
        // provider's worth of Timer per restart. Safe to dispose even if a connection accepted
        // just before shutdown is still finishing its handshake: Dispose only stops the polling
        // Timer, it never invalidates the already-loaded certificate Current still returns.
        _certificateProvider?.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _certificateProvider?.Dispose();
        base.Dispose();
    }
}
