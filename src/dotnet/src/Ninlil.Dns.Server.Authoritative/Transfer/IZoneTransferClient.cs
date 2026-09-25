using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This interface is the client side of <see cref="IZoneTransferService" />: it pulls a
///     zone by AXFR or IXFR from a primary. It needs only outbound sockets.
///     <para>
///         There is one implementation per <see cref="Transport" /> (TCP, TLS, QUIC), all
///         registered under this interface. <c>SecondaryZoneRefreshService</c> picks the one
///         matching the zone's <c>ZoneEntity.TransferTransport</c>.
///     </para>
/// </summary>
public interface IZoneTransferClient
{
    /// <summary>
    ///     This property contains the transport this implementation pulls over.
    /// </summary>
    ZoneTransferTransportEnum Transport { get; }

    /// <summary>
    ///     This method queries a primary's current SOA serial, without pulling anything.
    /// </summary>
    /// <param name="primaryAddress">The primary to query.</param>
    /// <param name="zoneName">The zone to ask about.</param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key to sign the query with, or <see langword="null" /> to send it
    ///     unsigned.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The primary's current serial, or <see langword="null" /> if it could not be reached.</returns>
    Task<uint?> QuerySoaSerialAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        string tsigKeyName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method pulls a transfer from a primary.
    /// </summary>
    /// <param name="primaryAddress">The primary to pull from.</param>
    /// <param name="zoneName">The zone to pull.</param>
    /// <param name="currentSerial">
    ///     This server's current serial for the zone, to request IXFR; or <see langword="null" />
    ///     when there is no local copy yet, to request AXFR.
    /// </param>
    /// <param name="tsigKeyName">
    ///     The key to sign the request with and to verify every response against; or
    ///     <see langword="null" /> to send it unsigned and accept an unsigned response.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The outcome of the pull.</returns>
    Task<ZoneTransferPullResult> PullAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        uint? currentSerial,
        string tsigKeyName,
        CancellationToken stoppingToken = default
    );
}
