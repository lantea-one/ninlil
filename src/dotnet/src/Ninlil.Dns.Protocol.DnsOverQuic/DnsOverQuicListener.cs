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
using Ninlil.Dns.Server.Core.Pipeline;
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
///         0-RTT is not enabled: no TLS session resumption is configured on
///         <see cref="SslServerAuthenticationOptions" />, which 0-RTT would need. RFC 9250
///         discusses the replay risk it brings.
///     </para>
///     <para>
///         There is no limit on the number of connections, only on streams per connection
///         (<see cref="DnsOverQuicOptions.MaxConcurrentBidirectionalStreams" />).
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

            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    /// <summary>
    ///     This method serves one accepted QUIC connection, handling every stream the peer opens
    ///     concurrently; each stream is an independent query (RFC 9250 §4.2). Streams are
    ///     separate, so unlike DNS over TLS no per-connection write lock is needed.
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
                    // from an abrupt reset), so there is nothing left to accept.
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
    ///     This method answers the query on one stream: it reads the length-prefixed message (the
    ///     client then closes its side of the stream, so nothing more needs reading), routes a
    ///     zone transfer to <see cref="HandleZoneTransferAsync" /> and any other query to the
    ///     pipeline, whose response is written back framed the same way before the server closes
    ///     its side (RFC 9250 §4.2). A message that does not decode is logged and the stream
    ///     dropped. The source address is the QUIC connection's remote address.
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
    ///     This method answers an AXFR or IXFR query on its stream with the sequence of messages
    ///     from <see cref="IZoneTransferService" />, the DNS-over-QUIC counterpart of the transfer
    ///     handling in <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c> and
    ///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c>.
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

            // Every response message goes on this stream, since RFC 9250 §4.2 gives each query
            // one stream and a transfer is the answer to one query. Only the last message closes
            // the stream's write side, so each message is held back one step until the next
            // arrives.
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
    ///     This method writes one length-prefixed message that is not the last in a sequence,
    ///     leaving the stream's write side open. Only <see cref="HandleZoneTransferAsync" /> uses
    ///     it.
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
    ///     This method writes one length-prefixed response and closes the stream's write side. No
    ///     write lock is needed, since the stream carries one query and is never reused (RFC 9250
    ///     §4.2).
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
