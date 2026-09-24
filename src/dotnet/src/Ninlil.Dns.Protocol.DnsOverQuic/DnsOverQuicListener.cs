using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic;

/// <summary>
///     This class implements RFC 9250's DNS-over-QUIC transport: a 2-octet length-prefixed DNS
///     message per QUIC stream, one dedicated bidirectional stream per query (RFC 9250 §4.2) --
///     never reused for a second query -- with TLS 1.3 already built into the QUIC handshake
///     itself, unlike DNS-over-TLS's separate TCP-then-TLS handshake. Also answers zone transfer
///     over the same stream shape: an AXFR/IXFR query is recognized (see
///     <see cref="HandleZoneTransferAsync" />) and answered as a real multi-message sequence
///     through <see cref="IZoneTransferService" />, every subsequent message written onto that
///     same stream rather than opening a new one -- RFC 9250 itself only defines an ordinary
///     stub-resolver query per stream, but this project applies the identical framing to a
///     multi-message transfer response, the same analogous extension
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>'s own RFC 9103 XFR-over-TLS support
///     makes of that transport.
///     <para>
///         0-RTT is deliberately left disabled: this class never configures TLS session
///         resumption/PSK support on <see cref="SslServerAuthenticationOptions" />, which is
///         what 0-RTT would require -- RFC 9250 itself warns against it due to replay risk.
///     </para>
/// </summary>
/// <param name="pipeline">The pipeline every ordinary (non-transfer) query is enqueued onto.</param>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="options">
///     This transport's own listen port, stream limits, and certificate -- read via
///     <see cref="IOptionsMonitor{TOptions}" /> so a REST-driven <c>enable</c> toggle is genuinely
///     observed the next time <see cref="ExecuteAsync" /> runs, the same reasoning
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>'s own <c>options</c>
///     parameter documents.
/// </param>
/// <param name="logger">The logger this listener records startup failures and connection-level failures to.</param>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class DnsOverQuicListener(
    DnsQueryProcessingPipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsOverQuicOptions> options,
    ILogger<DnsOverQuicListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains RFC 9250 §7.1's registered ALPN token for DNS-over-QUIC.
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <summary>
    ///     This property contains the server certificate presented to every connecting client --
    ///     re-checked periodically against disk, so a certificate an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces mid-run is picked up without a restart.
    /// </summary>
    private ReloadableTlsCertificateProvider _certificateProvider;

    /// <summary>
    ///     This property reports whether this listener's own <see cref="QuicListener" /> is
    ///     currently bound and accepting connections -- read by the <c>/status</c> health-check
    ///     endpoint, which needs a real "am I bound and running" signal from every transport
    ///     rather than just the fact that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        if (!QuicListener.IsSupported)
        {
            // QUIC needs libmsquic (and, on Linux, OpenSSL) present as a native OS dependency --
            // neither ships inside the .NET runtime itself, unlike every other transport this
            // project has built so far. Failing loudly here, without taking the rest of the
            // host down, is deliberately safer than letting a confusing low-level QuicException
            // surface later.
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

        QuicListenerOptions listenerOptions = new()
        {
            ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, options.CurrentValue.Port),
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
                        // TlsCertificateOptions.AllowedProtocols is deliberately not applied
                        // here: QUIC mandates TLS 1.3 unconditionally at the transport level
                        // (RFC 9001), so restricting EnabledSslProtocols would be meaningless at
                        // best -- and this has not been verified not to throw for QUIC's own
                        // handshake path, unlike the plain-TCP-based SslStream path DoT uses.
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [DoqApplicationProtocol],
                            ServerCertificate = _certificateProvider.Current,
                        },
                    }
                ),
        };

        await using QuicListener listener = await QuicListener.ListenAsync(
            listenerOptions,
            stoppingToken
        );
        IsListening = true;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                QuicConnection connection;

                try
                {
                    connection = await listener.AcceptConnectionAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = HandleConnectionAsync(connection, stoppingToken);
            }
        }
        finally
        {
            IsListening = false;
        }
    }

    /// <summary>
    ///     This method owns a single accepted QUIC connection for its entire lifetime,
    ///     accepting every bidirectional stream the peer opens -- each one an entirely
    ///     independent query, per RFC 9250 §4.2 -- concurrently, since QUIC's own stream
    ///     multiplexing means (unlike DNS-over-TLS's single shared byte stream) no per-connection
    ///     write-lock is needed at all.
    /// </summary>
    private async Task HandleConnectionAsync(
        QuicConnection connection,
        CancellationToken stoppingToken
    )
    {
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
                    // from an abrupt reset) -- either way, there is nothing left to accept.
                    break;
                }

                _ = HandleStreamAsync(connection, stream, stoppingToken);
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method answers a single query carried on its own dedicated stream: reads the
    ///     2-octet length-prefixed message (the client itself signals "done sending" via the
    ///     stream's own FIN, per RFC 9250 §4.2.1, so nothing further needs reading once the
    ///     declared length is satisfied), enqueues it onto the shared pipeline, and writes the
    ///     response back the same way, closing the stream's write side once the response has
    ///     been fully sent (RFC 9250 §4.2.2).
    /// </summary>
    private async Task HandleStreamAsync(
        QuicConnection connection,
        QuicStream stream,
        CancellationToken stoppingToken
    )
    {
        try
        {
            byte[] lengthPrefix = new byte[2];

            if (!await ReadExactAsync(stream, lengthPrefix, stoppingToken))
                return;

            ushort messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
            byte[] messageBuffer = new byte[messageLength];

            if (!await ReadExactAsync(stream, messageBuffer, stoppingToken))
                return;

            DnsMessage request;
            int tsigRecordStartOffset;

            try
            {
                request = DnsMessageReader.Read(messageBuffer, out tsigRecordStartOffset);
            }
            catch (FormatException exception)
            {
                logger.LogWarning(
                    exception,
                    "Discarding a malformed DNS-over-QUIC message from {RemoteAddress}.",
                    connection.RemoteEndPoint.Address
                );
                await stream.DisposeAsync();
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
                    stoppingToken
                );
                return;
            }

            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    messageBuffer,
                    DnsTransportProtocolEnum.Quic,
                    connection.RemoteEndPoint.Address,
                    (responseBytes, sendToken) =>
                        WriteResponseAsync(stream, responseBytes, sendToken)
                ),
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "DNS-over-QUIC stream from {Endpoint} ended abnormally.",
                connection.RemoteEndPoint
            );
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method answers a real multi-message AXFR/IXFR sequence on this same stream,
    ///     through <see cref="IZoneTransferService" /> -- the RFC 9250-analogous counterpart to
    ///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>'s own RFC 9103 XFR-over-TLS
    ///     support, and to <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>'s own original AXFR/IXFR
    ///     special case.
    /// </summary>
    private async Task HandleZoneTransferAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsResourceRecordTypeEnum questionType,
        QuicStream stream,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IZoneTransferService transferService =
                scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

            DnsQueryContext context = new(
                request,
                DnsTransportProtocolEnum.Quic,
                remoteAddress,
                DateTimeOffset.UtcNow,
                rawMessage,
                tsigRecordStartOffset
            );

            IAsyncEnumerable<DnsMessage> responses =
                questionType == DnsResourceRecordTypeEnum.Axfr
                    ? transferService.CreateAxfrResponseAsync(context, stoppingToken)
                    : transferService.CreateIxfrResponseAsync(context, stoppingToken);

            // Every response message is written onto this same stream -- RFC 9250 §4.2 dedicates
            // one stream per query, and a multi-message transfer response is still, structurally,
            // the answer to the single query this stream carried; only the very last message
            // actually closes the stream's write side (RFC 9250 §4.2.2).
            byte[] pendingMessageBytes = null;

            await foreach (DnsMessage responseMessage in responses.WithCancellation(stoppingToken))
            {
                if (pendingMessageBytes is not null)
                    await WriteFramedNotLastAsync(stream, pendingMessageBytes, stoppingToken);

                pendingMessageBytes = DnsMessageWriter.Write(responseMessage);
            }

            if (pendingMessageBytes is not null)
            {
                byte[] lengthPrefix = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(
                    lengthPrefix,
                    (ushort)pendingMessageBytes.Length
                );
                await stream.WriteAsync(lengthPrefix, stoppingToken);
                await stream.WriteAsync(pendingMessageBytes, completeWrites: true, stoppingToken);
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method writes one length-prefixed message that is not the last one in a sequence
    ///     -- used only by <see cref="HandleZoneTransferAsync" />'s own multi-message loop, which
    ///     buffers one message behind so it always knows, at write time, whether the one it is
    ///     about to write is the final one (and must therefore close the stream's write side) or
    ///     not.
    /// </summary>
    private static async ValueTask WriteFramedNotLastAsync(
        QuicStream stream,
        byte[] messageBytes,
        CancellationToken stoppingToken
    )
    {
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)messageBytes.Length);

        await stream.WriteAsync(lengthPrefix, stoppingToken);
        await stream.WriteAsync(messageBytes, stoppingToken);
    }

    /// <summary>
    ///     This method writes a single length-prefixed response and closes the stream's write
    ///     side -- no write-lock needed, since this stream belongs to exactly one query and is
    ///     never reused for another (RFC 9250 §4.2.2 explicitly forbids that).
    /// </summary>
    private static async ValueTask WriteResponseAsync(
        QuicStream stream,
        ReadOnlyMemory<byte> messageBytes,
        CancellationToken stoppingToken
    )
    {
        try
        {
            byte[] lengthPrefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)messageBytes.Length);

            await stream.WriteAsync(lengthPrefix, stoppingToken);
            await stream.WriteAsync(messageBytes, completeWrites: true, stoppingToken);
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method fills a buffer completely from a stream, or reports a clean end-of-stream.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> once <paramref name="buffer" /> is completely filled, or
    ///     <see langword="false" /> if the stream ended before the first byte of this read
    ///     arrived.
    /// </returns>
    /// <exception cref="IOException">
    ///     Thrown when the stream ends partway through an already-started read.
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

        // ExecuteAsync creates a brand-new ReloadableTlsCertificateProvider (and the polling
        // Timer inside it) on every restart, the same as
        // Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener's own certificate provider;
        // disposing only in this class's own Dispose() -- called once, at final host shutdown,
        // not on every mid-process stop -- would leak one provider's worth of Timer per restart.
        _certificateProvider?.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _certificateProvider?.Dispose();
        base.Dispose();
    }
}
