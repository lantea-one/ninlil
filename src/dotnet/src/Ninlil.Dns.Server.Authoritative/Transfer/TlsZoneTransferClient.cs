using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" /> with RFC 9103 zone transfer over
///     TLS: the exchange <see cref="TcpZoneTransferClient" /> performs, inside TLS, so an observer
///     cannot read the zone. TSIG still does the authentication; TLS adds confidentiality. The
///     SOA check (<see cref="QuerySoaSerialAsync" />) still goes over plain UDP (see
///     <see cref="ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync" />).
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     The port, timeouts and certificate check, read through
///     <see cref="IOptionsMonitor{TOptions}" /> on every pull.
/// </param>
/// <param name="logger">
///     The logger for the reason an unsigned or wrongly signed SOA answer is ignored. Optional.
/// </param>
public class TlsZoneTransferClient(
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferClientOptions> options,
    ILogger<TlsZoneTransferClient> logger = null
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
            stoppingToken,
            logger
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

        // The request MAC the first response chains from (see TcpZoneTransferClient).
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
            // See ZoneTransferClientOptions.ValidateServerCertificate.
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
