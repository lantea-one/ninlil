using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferClient" /> over plain TCP: an SOA query over
///     UDP for <see cref="QuerySoaSerialAsync" />, and an AXFR/IXFR pull with RFC 1035 §4.2.2
///     length framing for <see cref="PullAsync" />. <see cref="TlsZoneTransferClient" /> and
///     <see cref="QuicZoneTransferClient" /> share the protocol logic through
///     <see cref="ZoneTransferPullCodec" /> and differ only in the connection.
/// </summary>
/// <param name="tsigService">The service used to sign requests and verify responses.</param>
/// <param name="options">
///     The port and timeouts, read through <see cref="IOptionsMonitor{TOptions}" /> on every SOA
///     query and pull, so changes apply without a restart.
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

        // Keep the request MAC: the first response's TSIG must verify against it (RFC 8945
        // §5.3).
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
