using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
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
///     This transport's own listen port and provider name -- read via
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
    ///     This property contains the listening socket, once <see cref="ExecuteAsync" /> has
    ///     bound one -- kept here so <see cref="StopAsync" /> can close it, unblocking the
    ///     receive loop's own pending receive call during shutdown.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting datagrams -- read by the <c>/status</c> health-check endpoint, which needs a
    ///     real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

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

        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.CurrentValue.Port));
        IsListening = true;

        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await _socket.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.IPv6Any, 0),
                    stoppingToken
                );
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
                    SendResponseAsync(responseBytes, senderEndpoint, sendToken),
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method sends a single response packet back to whichever endpoint its query
    ///     arrived from.
    /// </summary>
    private async ValueTask SendResponseAsync(
        ReadOnlyMemory<byte> responseBytes,
        IPEndPoint senderEndpoint,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await _socket.SendToAsync(responseBytes, senderEndpoint, stoppingToken);
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
        _socket?.Close();
        await base.StopAsync(cancellationToken);
    }
}
