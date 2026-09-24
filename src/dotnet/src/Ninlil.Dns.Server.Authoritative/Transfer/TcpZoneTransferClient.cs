using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" />: a plain SOA query over UDP for
///     <see cref="QuerySoaSerialAsync" />, and an RFC 1035 §4.2.2-framed AXFR/IXFR pull over plain
///     TCP for <see cref="PullAsync" />. The confidentiality-adding counterparts,
///     <see cref="TlsZoneTransferClient" />/<see cref="QuicZoneTransferClient" />, share this
///     class's own wire-protocol logic via <see cref="ZoneTransferPullCodec" />, differing only in
///     how the underlying connection itself is established.
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     This client's own configuration, such as timeouts -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single pull this (scoped) client attempts, so a configuration write reaches the
///     very next SOA query or transfer with no restart.
/// </param>
public class TcpZoneTransferClient(
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferClientOptions> options
) : IZoneTransferClient
{
    /// <inheritdoc />
    public ZoneTransferTransportEnum Transport => ZoneTransferTransportEnum.Plain;

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

        // Captured before the query is serialized -- the first response message's own TSIG must
        // verify against this exact MAC per RFC 8945 §5.3, chaining the response back to the
        // specific request that produced it.
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
                options.CurrentValue.PrimaryPort,
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

        try
        {
            NetworkStream stream = tcpClient.GetStream();
            return await ZoneTransferPullOverStream.PullAsync(
                stream,
                queryBytes,
                tsigKeyName,
                requestMac,
                currentSerial,
                tsigService,
                options.CurrentValue.ReadTimeout,
                stoppingToken
            );
        }
        finally
        {
            tcpClient.Close();
        }
    }
}
