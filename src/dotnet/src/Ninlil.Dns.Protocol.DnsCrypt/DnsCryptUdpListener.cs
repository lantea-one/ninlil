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
///     This class implements the DNSCrypt UDP transport: one query per datagram and one response
///     per datagram, as in <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>. Each datagram goes
///     through <see cref="DnsCryptQueryHandler" />, which tells a plaintext certificate discovery
///     query from an encrypted client query, since DNSCrypt carries both on the same port.
///     An encrypted reply is never larger than its query: a longer answer is truncated (TC) so
///     the client retries over TCP.
/// </summary>
/// <param name="pipeline">The pipeline every decrypted query is enqueued onto.</param>
/// <param name="store">The store the tracked certificates are read from.</param>
/// <param name="options">
///     The listen addresses, port and provider name, read through
///     <see cref="IOptionsMonitor{TOptions}" /> so a changed <c>Enable</c> is seen when the
///     listener is started again (see the <c>options</c> parameter of
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>).
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
    ///     This constant contains the size of the receive buffer, the same value and reason as in
    ///     <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <summary>
    ///     This field contains the bound sockets, one per listen address (or a single dual-stack
    ///     one when none are configured). <see cref="StopAsync" /> closes them, which unblocks
    ///     each receive loop's pending receive.
    /// </summary>
    private ListenSocketSet _sockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This property reports whether at least one of this listener's sockets is bound and
    ///     receiving. <c>TransportListenersHealthCheck</c> reads it, since being registered or
    ///     configured does not mean a transport is running.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)", for the health check.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _sockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        if (string.IsNullOrWhiteSpace(options.CurrentValue.ProviderName))
        {
            // With a blank provider name the discovery name would sit directly under the root
            // rather than under a name only this resolver's clients ask about; not starting is
            // better than answering that.
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
                isDatagram: true,
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
