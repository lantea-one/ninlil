using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" />'s QUIC-carried variant -- the same
///     transport-confidentiality benefit as <see cref="TlsZoneTransferClient" />, over QUIC
///     instead of a TCP-then-TLS handshake, mirroring how <c>Ninlil.Dns.Protocol.DnsOverQuic</c>
///     extends <c>Ninlil.Dns.Protocol.DnsOverTls</c>'s own reasoning for ordinary queries. RFC
///     9250 itself only defines a stub-resolver query per dedicated bidirectional stream; this
///     class applies that same one-stream-per-exchange shape to the whole multi-message AXFR/IXFR
///     sequence instead, writing every response message onto the one stream the query itself
///     opened. Needs <c>libmsquic</c>/OpenSSL as a native OS dependency, exactly like
///     <c>DnsOverQuicListener</c> does -- see <see cref="PullAsync" />'s own
///     <see cref="QuicConnection.IsSupported" /> check.
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     This client's own configuration, such as timeouts and whether to validate the primary's own
///     certificate -- bound via <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via
///     <c>options.CurrentValue</c>, on every single pull this (scoped) client attempts.
/// </param>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicZoneTransferClient(
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferClientOptions> options
) : IZoneTransferClient
{
    /// <summary>
    ///     This constant contains RFC 9250 §7.1's registered ALPN token for DNS-over-QUIC, reused
    ///     here since no dedicated ALPN token exists for zone-transfer-over-QUIC specifically.
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <inheritdoc />
    public ZoneTransferTransportEnum Transport => ZoneTransferTransportEnum.Quic;

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
        if (!QuicConnection.IsSupported)
            return ZoneTransferPullResult.Failure(
                "QUIC is not supported on this platform -- libmsquic (and, on Linux, OpenSSL) must be installed."
            );

        DnsMessage query = ZoneTransferPullCodec.BuildTransferQuery(zoneName, currentSerial);

        if (!string.IsNullOrEmpty(tsigKeyName))
            query = await tsigService.SignAsync(query, tsigKeyName, query.Id, stoppingToken);

        // Captured before the query is serialized -- see TcpZoneTransferClient's own identical
        // comment for why this must be the exact MAC the first response message chains from.
        byte[] requestMac = query.Tsig?.Mac;
        byte[] queryBytes = DnsMessageWriter.Write(query);

        QuicClientConnectionOptions connectionOptions = new()
        {
            RemoteEndPoint = new IPEndPoint(primaryAddress, options.CurrentValue.PrimaryQuicPort),
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [DoqApplicationProtocol],
                TargetHost = primaryAddress.ToString(),
                // See TlsZoneTransferClient's own identical reasoning on ValidateServerCertificate.
                RemoteCertificateValidationCallback = options.CurrentValue.ValidateServerCertificate
                    ? null
                    : (_, _, _, _) => true,
            },
        };

        QuicConnection connection;

        try
        {
            connection = await QuicConnection
                .ConnectAsync(connectionOptions, stoppingToken)
                .AsTask()
                .WaitAsync(options.CurrentValue.ConnectTimeout, stoppingToken);
        }
        catch (Exception exception)
            when (exception
                    is QuicException
                        or AuthenticationException
                        or OperationCanceledException
                        or TimeoutException
            )
        {
            return ZoneTransferPullResult.Failure(
                $"Could not establish a QUIC connection to primary {primaryAddress}: {exception.Message}"
            );
        }

        await using (connection)
        {
            await using QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional,
                stoppingToken
            );

            await ZoneTransferPullOverStream.WriteQueryAsync(stream, queryBytes, stoppingToken);

            // RFC 9250 §4.2.1: the client signals it is done sending by closing this stream's own
            // write side -- sent here, between the query write and reading the response
            // sequence, since this is a single-request-then-many-responses exchange (this client
            // never sends a second query on the same stream).
            await stream.WriteAsync(
                ReadOnlyMemory<byte>.Empty,
                completeWrites: true,
                stoppingToken
            );

            return await ZoneTransferPullOverStream.ReadResponsesAsync(
                stream,
                tsigKeyName,
                requestMac,
                currentSerial,
                tsigService,
                options.CurrentValue.ReadTimeout,
                stoppingToken
            );
        }
    }
}
