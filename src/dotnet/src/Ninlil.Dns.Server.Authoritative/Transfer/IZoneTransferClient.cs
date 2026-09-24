using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This interface is the client-side mirror of <see cref="IZoneTransferService" />: instead
///     of serving AXFR/IXFR out, it pulls one in from another server's primary copy of a zone.
///     Like <see cref="Notify.INotifySender" />, this is a real network client that needs nothing
///     beyond outbound sockets, independent of any inbound transport such as
///     <c>Ninlil.Dns.Protocol.Plain</c>, since initiating a pull is inherently this server acting
///     as a client, not a listener.
///     <para>
///         Every implementation is registered in DI against this same interface (see
///         <c>ServiceCollectionAuthoritativeExtension</c>), one per <see cref="Transport" /> value
///         -- <c>SecondaryZoneRefreshService</c> resolves the full <see cref="IEnumerable{T}" />
///         of them and selects the one whose <see cref="Transport" /> matches a zone's own
///         <c>ZoneEntity.TransferTransport</c>, the same "resolve every registered implementation
///         and pick by an enum key" shape <c>ForwarderQueryResolver</c>'s/
///         <c>ForwarderHealthTracker</c>'s own <c>_clientsByProtocol</c> dictionaries already
///         establish for upstream forwarder clients.
///     </para>
/// </summary>
public interface IZoneTransferClient
{
    /// <summary>
    ///     This property contains which wire transport this implementation pulls a transfer over.
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
    ///     This server's own current serial for the zone, requesting an incremental transfer
    ///     (IXFR); or <see langword="null" />, when the zone has no local copy at all yet,
    ///     requesting a full one (AXFR) instead.
    /// </param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key to sign the request with, and to require every response to verify
    ///     against; or <see langword="null" /> to send the request unsigned and accept an
    ///     unsigned response.
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
