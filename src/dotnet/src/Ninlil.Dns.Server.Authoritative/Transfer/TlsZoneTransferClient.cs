using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" />'s RFC 9103 XFR-over-TLS variant:
///     the same AXFR/IXFR exchange <see cref="TcpZoneTransferClient" /> performs, carried inside a
///     client-side TLS session instead of a bare TCP one, so a network observer between this
///     server and the primary cannot see the transferred zone content. TSIG remains the actual
///     authentication layer, unchanged -- this only adds transport confidentiality on top, per
///     this feature's own scope (see <c>ZoneEntity.TransferTransport</c>'s own doc comment).
///     <see cref="QuerySoaSerialAsync" />'s own lightweight SOA probe deliberately still goes out
///     over plain UDP, unencrypted, the same reasoning <see cref="ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync" />'s
///     own doc comment gives.
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     This client's own configuration, such as timeouts and whether to validate the primary's own
///     certificate -- bound via <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via
///     <c>options.CurrentValue</c>, on every single pull this (scoped) client attempts.
/// </param>
public class TlsZoneTransferClient(
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferClientOptions> options
) : IZoneTransferClient
{
    /// <inheritdoc />
    public ZoneTransferTransportEnum Transport => ZoneTransferTransportEnum.Tls;

    /// <inheritdoc />
    public Task<uint?> QuerySoaSerialAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        string tsigKeyName,
        CancellationToken stoppingToken = default
    ) =>
        ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync(
            primaryAddress,
            zoneName,
            tsigKeyName,
            options.CurrentValue.PrimaryPort,
            options.CurrentValue.SoaQueryTimeout,
            tsigService,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<ZoneTransferPullResult> PullAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        uint? currentSerial,
        string tsigKeyName,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage query = ZoneTransferPullCodec.BuildTransferQuery(zoneName, currentSerial);

        if (!string.IsNullOrEmpty(tsigKeyName))
            query = await tsigService.SignAsync(query, tsigKeyName, query.Id, stoppingToken);

        // Captured before the query is serialized -- see TcpZoneTransferClient's own identical
        // comment for why this must be the exact MAC the first response message chains from.
        byte[] requestMac = query.Tsig?.Mac;
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using TcpClient tcpClient = new(primaryAddress.AddressFamily);

        try
        {
            using CancellationTokenSource connectTimeoutCts = new(
                options.CurrentValue.ConnectTimeout
            );
            using CancellationTokenSource connectLinkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    connectTimeoutCts.Token
                );

            await tcpClient.ConnectAsync(
                primaryAddress,
                options.CurrentValue.PrimaryTlsPort,
                connectLinkedCts.Token
            );
        }
        catch (Exception exception)
            when (exception is SocketException or OperationCanceledException)
        {
            return ZoneTransferPullResult.Failure(
                $"Could not connect to primary {primaryAddress}: {exception.Message}"
            );
        }

        await using SslStream sslStream = new(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            // ValidateServerCertificate defaults true (real, OS-trust-store validation); an
            // operator can set it false only for a homelab primary presenting a certificate this
            // secondary has no other way to trust -- see ZoneTransferClientOptions's own doc
            // comment on ValidateServerCertificate for the full reasoning.
            options.CurrentValue.ValidateServerCertificate
                ? null
                : (_, _, _, _) => true
        );

        try
        {
            using CancellationTokenSource handshakeTimeoutCts = new(
                options.CurrentValue.ConnectTimeout
            );
            using CancellationTokenSource handshakeLinkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    handshakeTimeoutCts.Token
                );

            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = primaryAddress.ToString() },
                handshakeLinkedCts.Token
            );
        }
        catch (Exception exception)
            when (exception is AuthenticationException or IOException or OperationCanceledException)
        {
            return ZoneTransferPullResult.Failure(
                $"TLS handshake with primary {primaryAddress} failed: {exception.Message}"
            );
        }

        return await ZoneTransferPullOverStream.PullAsync(
            sslStream,
            queryBytes,
            tsigKeyName,
            requestMac,
            currentSerial,
            tsigService,
            options.CurrentValue.ReadTimeout,
            stoppingToken
        );
    }
}
