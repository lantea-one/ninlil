using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class implements DNSCrypt's UDP transport: one query per datagram, one response per
///     datagram back -- structurally the same shape as
///     <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>, except every datagram is
///     first routed through <see cref="DnsCryptQueryHandler" /> to tell a plaintext certificate
///     discovery query apart from an encrypted client query, since (unlike every other transport
///     this project has built) DNSCrypt carries both shapes over the very same port.
/// </summary>
/// <param name="pipeline">The pipeline every decrypted query is enqueued onto.</param>
/// <param name="store">The store this resolver's own tracked certificates are read from.</param>
/// <param name="options">
///     This transport's own listen addresses, port and provider name -- read via
///     <see cref="IOptionsMonitor{TOptions}" /> so a REST-driven <c>enable</c> toggle is genuinely
///     observed the next time <see cref="ExecuteAsync" /> runs, the same reasoning
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>'s own <c>options</c>
///     parameter documents.
/// </param>
/// <param name="logger">The logger this listener records unexpected socket failures to.</param>
public sealed class DnsCryptUdpListener(
    DnsQueryProcessingPipeline pipeline,
    IDnsCryptCertificateStore store,
    IOptionsMonitor<DnsCryptOptions> options,
    ILogger<DnsCryptUdpListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this listener will ever read, the
    ///     same reasoning and value <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>
    ///     already uses.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <summary>
    ///     This property contains the bound sockets, one per listen address (or a single
    ///     dual-stack one when none are configured) -- kept here so <see cref="StopAsync" /> can
    ///     close them, unblocking each receive loop's pending receive call during shutdown.
    /// </summary>
    private ListenSocketSet _sockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting datagrams -- read by the <c>/status</c> health-check endpoint, which needs a
    ///     real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)" -- read by the <c>/status</c> health check.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _sockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        if (string.IsNullOrWhiteSpace(options.CurrentValue.ProviderName))
        {
            // A blank provider name would make every certificate discovery query resolve
            // against the root name instead of a name only this resolver's own clients would
            // ever ask about -- silently answering that is worse than not starting at all.
            logger.LogError(
                "DNSCrypt is enabled, but no ProviderName is configured -- clients would have no name to discover this resolver's certificates under. This transport will not start; every other part of this server is unaffected."
            );
            return;
        }

        DnsName providerCertificateName =
            DnsCryptCertificateDiscoveryResponder.BuildProviderCertificateName(
                options.CurrentValue.ProviderName
            );

        _sockets = ListenSocketSet.Bind(
            "DNSCrypt (UDP)",
            options.CurrentValue.ListenAddresses,
            options.CurrentValue.Port,
            ProtocolType.Udp,
            logger
        );

        if (_sockets.Sockets.Count == 0)
            return;

        IsListening = true;

        await Task.WhenAll(
            _sockets.Sockets.Select(socket =>
                ReceiveLoopAsync(socket, providerCertificateName, stoppingToken)
            )
        );
    }

    /// <summary>
    ///     This method receives packets on one socket until shutdown. Each listen address gets its
    ///     own loop, and every response leaves through the socket its query arrived on.
    /// </summary>
    private async Task ReceiveLoopAsync(
        Socket socket,
        DnsName providerCertificateName,
        CancellationToken stoppingToken
    )
    {
        byte[] receiveBuffer = new byte[MaximumDatagramSize];
        EndPoint anySender = new IPEndPoint(
            socket.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any,
            0
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await socket.ReceiveFromAsync(receiveBuffer, anySender, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                logger.LogWarning(exception, "Error receiving a DNSCrypt UDP datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint senderEndpoint = (IPEndPoint)result.RemoteEndPoint;

            await DnsCryptQueryHandler.HandlePacketAsync(
                datagram,
                senderEndpoint.Address,
                DnsTransportProtocolEnum.DnsCrypt,
                providerCertificateName,
                pipeline,
                store,
                logger,
                (responseBytes, sendToken) =>
                    SendResponseAsync(socket, responseBytes, senderEndpoint, sendToken),
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method sends a single response packet back to whichever endpoint its query
    ///     arrived from.
    /// </summary>
    private async ValueTask SendResponseAsync(
        Socket socket,
        ReadOnlyMemory<byte> responseBytes,
        IPEndPoint senderEndpoint,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await socket.SendToAsync(responseBytes, senderEndpoint, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a DNSCrypt UDP response to {Endpoint}.",
                senderEndpoint
            );
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _sockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
