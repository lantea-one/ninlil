using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class implements the DNSCrypt TCP transport, which clients fall back to: the
///     two-octet big-endian length prefix of DNS over TCP (RFC 1035 §4.2.2, RFC 7766) around
///     either a plaintext certificate discovery query or an encrypted client query, told apart
///     by <see cref="DnsCryptQueryHandler" /> as in <see cref="DnsCryptUdpListener" />. A
///     connection stays open for further queries until
///     <c>DnsCryptOptions.TcpIdleTimeout</c> passes with no complete packet, or the client
///     disconnects.
/// </summary>
/// <param name="pipeline">The pipeline every decrypted query is enqueued onto.</param>
/// <param name="store">The store the tracked certificates are read from.</param>
/// <param name="options">
///     The listen addresses, port, provider name and connection limits, read through
///     <see cref="IOptionsMonitor{TOptions}" /> so a changed <c>Enable</c> is seen when the
///     listener is started again (see the <c>options</c> parameter of
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>).
/// </param>
/// <param name="logger">The logger this listener records connection-level failures to.</param>
/// <param name="rateLimiter">
///     The limiter a certificate discovery query passes, as every pipeline query does. When it is
///     <see langword="null" />, discovery is not limited.
/// </param>
public sealed class DnsCryptTcpListener(
    DnsQueryProcessingPipeline pipeline,
    IDnsCryptCertificateStore store,
    IOptionsMonitor<DnsCryptOptions> options,
    ILogger<DnsCryptTcpListener> logger,
    IDnsRateLimiter rateLimiter = null
) : BackgroundService
{
    /// <summary>
    ///     This field contains the listening sockets, one per listen address (or a single
    ///     dual-stack one when none are configured). <see cref="StopAsync" /> closes them, which
    ///     unblocks each accept loop's pending accept.
    /// </summary>
    private ListenSocketSet _listenSockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This field limits how many connections are served at once, across all listen
    ///     addresses (<c>DnsCryptOptions.MaxConcurrentTcpConnections</c>); a connection beyond the
    ///     limit is closed at once.
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
        if (!options.CurrentValue.Enable)
            return;

        if (string.IsNullOrWhiteSpace(options.CurrentValue.ProviderName))
        {
            // DnsCryptUdpListener logs the same error. Both listeners share one Enable and one
            // ProviderName, so both refuse to start rather than one answering alone.
            logger.LogError(
                "DNSCrypt is enabled, but no ProviderName is configured -- clients would have no name to discover this resolver's certificates under. This transport will not start; every other part of this server is unaffected."
            );
            return;
        }

        if (
            !DnsCryptCertificateDiscoveryResponder.TryBuildProviderCertificateName(
                options.CurrentValue.ProviderName,
                out DnsName providerCertificateName
            )
        )
        {
            // An unparseable name must not throw out of ExecuteAsync: a faulted hosted service
            // can stop the host. Log and stay unbound instead, like a blank name.
            logger.LogError(
                "DNSCrypt is enabled, but ProviderName '{ProviderName}' is not a valid domain name (at most 255 octets, labels of at most 63 octets) -- clients would have no name to discover this resolver's certificates under. This transport will not start; every other part of this server is unaffected.",
                options.CurrentValue.ProviderName
            );
            return;
        }

        _connectionLimiter = new SemaphoreSlim(options.CurrentValue.MaxConcurrentTcpConnections);

        _listenSockets = ListenSocketSet.Bind(
            "DNSCrypt (TCP)",
            options.CurrentValue.ListenAddresses,
            options.CurrentValue.Port,
            ProtocolType.Tcp,
            logger
        );

        if (_listenSockets.Sockets.Count == 0)
            return;

        IsListening = true;

        await Task.WhenAll(
            _listenSockets.Sockets.Select(socket =>
                AcceptLoopAsync(socket, providerCertificateName, stoppingToken)
            )
        );
    }

    /// <summary>
    ///     This method accepts connections on one listening socket until shutdown; each listen
    ///     address gets its own loop, all sharing one connection limit.
    /// </summary>
    private async Task AcceptLoopAsync(
        Socket listenSocket,
        DnsName providerCertificateName,
        CancellationToken stoppingToken
    )
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
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, providerCertificateName, stoppingToken);
        }
    }

    /// <summary>
    ///     This method serves one accepted connection through a
    ///     <see cref="FramedDnsStreamSession" />: it routes each length-prefixed packet through
    ///     <see cref="DnsCryptQueryHandler" /> until <c>DnsCryptOptions.TcpIdleTimeout</c> passes
    ///     with no complete packet, the client disconnects, or a write to the client misses that
    ///     same timeout. The session owns the per-connection limits (16 queries in flight, one
    ///     writer task); a query still outstanding when the connection goes idle is answered
    ///     first.
    /// </summary>
    private async Task HandleConnectionAsync(
        Socket connection,
        DnsName providerCertificateName,
        CancellationToken stoppingToken
    )
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;

        try
        {
            using NetworkStream stream = new(connection, ownsSocket: false);
            FramedDnsStreamSession session = new(
                stream,
                remoteEndpoint.Address,
                options.CurrentValue.TcpIdleTimeout,
                logger,
                stoppingToken
            );

            await session.RunAsync(
                async (packetBuffer, _, sessionToken) =>
                    await DnsCryptQueryHandler.HandlePacketAsync(
                        packetBuffer,
                        remoteEndpoint.Address,
                        DnsTransportProtocolEnum.DnsCrypt,
                        isDatagram: false,
                        providerCertificateName,
                        pipeline,
                        store,
                        logger,
                        // Only a certificate discovery answer takes this path; queued without a
                        // slot, since the packet's slot is released when the handler returns.
                        (responseBytes, sendToken) => session.SendAsync(responseBytes, sendToken),
                        sessionToken,
                        session.CreateResponder(),
                        session.CreateDropNotifier(),
                        rateLimiter
                    )
                        ? FrameOutcomeEnum.Enqueued
                        : FrameOutcomeEnum.Handled
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "DNSCrypt TCP connection from {Endpoint} ended abnormally.",
                remoteEndpoint
            );
        }
        finally
        {
            connection.Close();
            _connectionLimiter.Release();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _listenSockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
