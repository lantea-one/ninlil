// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence;

/// <summary>
///     This class holds the tunables for passive client presence tracking -- how recently a known
///     client must have been seen to count as online, and how often sightings are persisted and
///     the kernel neighbor table is sampled. Every value is read live through
///     <c>IOptionsMonitor</c>, so a change written through the settings REST surface takes effect
///     without a restart.
///     <para>
///         Presence is passive: it is inferred from signals this server already observes
///         (confirmed-reachable kernel neighbor entries, DHCP lease grants and renewals, and live
///         DNS queries), never from active network scanning. Online/offline is never stored; it is
///         computed at read time by comparing an address's last sighting against
///         <see cref="OnlineWindow" />, so it self-corrects the moment a device goes quiet, with no
///         sweep required.
///     </para>
/// </summary>
public sealed class ClientPresenceOptions
{
    /// <summary>
    ///     This constant contains the configuration section these options are bound from.
    /// </summary>
    public const string SectionName = "DnsServer:ClientPresence";

    /// <summary>
    ///     This property contains how recently a client must have been seen to be reported
    ///     online. Short enough to notice a phone leaving, long enough that an idle-but-present
    ///     device making only occasional DNS queries stays online between them.
    /// </summary>
    public TimeSpan OnlineWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     This property contains how often buffered sightings are written to storage -- the
    ///     worst-case delay before a sighting becomes visible through the REST API.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property indicates whether the kernel neighbor table (ARP for IPv4, NDP for IPv6)
    ///     is sampled as a presence signal. On by default: it is read-only, sends nothing on the
    ///     network, and covers a device that neither leases from this server nor queries it. It
    ///     is silently inert on a platform without Linux's rtnetlink interface.
    /// </summary>
    public bool EnableNeighborTableSignal { get; set; } = true;

    /// <summary>
    ///     This property contains how often the kernel neighbor table is sampled. Only entries the
    ///     kernel currently holds as confirmed-reachable count, and the kernel keeps that state for
    ///     roughly half a minute after its last confirmation, so a sampling interval well under
    ///     that catches every recently active device.
    /// </summary>
    public TimeSpan NeighborTablePollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This method reports whether a client last seen at <paramref name="lastSeenUtc" /> counts
    ///     as online at <paramref name="nowUtc" />.
    /// </summary>
    /// <param name="lastSeenUtc">The most recent sighting, or <see langword="null" /> if never seen.</param>
    /// <param name="nowUtc">The instant to evaluate at.</param>
    /// <returns><see langword="true" /> when the sighting falls within <see cref="OnlineWindow" />.</returns>
    public bool IsOnline(DateTimeOffset? lastSeenUtc, DateTimeOffset nowUtc) =>
        lastSeenUtc is { } lastSeen && nowUtc - lastSeen <= OnlineWindow;
}
