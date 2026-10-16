using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" /> over QUIC (DNS-over-QUIC, RFC
///     9250), giving the same confidentiality as <see cref="TlsZoneTransferClient" />. The query
///     goes on one bidirectional stream and every response message comes back on it, as RFC
///     9250 §4.2 allows for zone transfers. It needs the native <c>libmsquic</c> library, like
///     <c>DnsOverQuicListener</c>; <see cref="PullAsync" /> checks
///     <see cref="QuicConnection.IsSupported" />. The SOA check goes over plain UDP.
///     <para>
///         Every message on a QUIC connection has ID 0 (RFC 9250 §4.2.1), so the query is sent
///         with ID 0 and signed with original ID 0 (the TSIG MAC covers the ID as sent, RFC
///         8945 §4.3). A response message with another ID ends the pull with a failure; the
///         primary treats a non-zero ID as a protocol error and closes the connection.
///     </para>
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     The port, timeouts and certificate check, read through
///     <see cref="IOptionsMonitor{TOptions}" /> on every pull.
/// </param>
/// <param name="logger">
///     The logger for the reason an unsigned or wrongly signed SOA answer is ignored. Optional.
/// </param>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicZoneTransferClient(
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferClientOptions> options,
    ILogger<QuicZoneTransferClient> logger = null
) : IZoneTransferClient
{
    /// <summary>
    ///     This constant contains the DNS-over-QUIC ALPN token from RFC 9250 §7.1, which zone
    ///     transfers over QUIC also use.
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <summary>
    ///     This constant contains the message ID of every DNS-over-QUIC message (RFC 9250
    ///     §4.2.1): the query's ID, its TSIG original ID and the ID each response must carry.
    /// </summary>
    private const ushort QuicMessageId = 0;

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
        if (!QuicConnection.IsSupported)
            return ZoneTransferPullResult.Failure(
                "QUIC is not supported on this platform -- libmsquic (and, on Linux, OpenSSL) must be installed."
            );

        DnsMessage query = ZoneTransferPullCodec.BuildTransferQuery(
            zoneName,
            currentSerial,
            QuicMessageId
        );

        if (!string.IsNullOrEmpty(tsigKeyName))
            query = await tsigService.SignAsync(query, tsigKeyName, QuicMessageId, stoppingToken);

        // The request MAC the first response chains from (see TcpZoneTransferClient).
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
                // See ZoneTransferClientOptions.ValidateServerCertificate.
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

            // RFC 9250 §4.2: close the write side after the query to show nothing more will be
            // sent, then read the responses.
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
                stoppingToken,
                QuicMessageId
            );
        }
    }
}
