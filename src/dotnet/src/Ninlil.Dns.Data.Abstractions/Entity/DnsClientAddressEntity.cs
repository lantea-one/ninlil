using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one persistent identifier a single real-world device
///     (<see cref="DnsClientEntity" />) is known by -- a colon-separated hardware (MAC) address for
///     an <see cref="InternetProtocolVersionEnum.IPv4" /> identity, or a DHCP Unique Identifier
///     (DUID) for an <see cref="InternetProtocolVersionEnum.IPv6" /> one. A single device is a
///     one-to-many parent of these rows rather than carrying an identifier directly itself,
///     specifically so a genuinely dual-stack device -- one with both an IPv4 MAC address seen over
///     ARP/DHCPv4 and a wholly different IPv6 DUID seen over DHCPv6 -- is tracked as <em>one</em>
///     <see cref="DnsClientEntity" /> row with two addresses here, rather than the two disconnected,
///     same-device rows an earlier, narrower design (a single identifier/protocol pair carried
///     directly on <see cref="DnsClientEntity" /> itself) used to force.
///     <para>
///         <strong>Uniqueness</strong>: the pair (<see cref="ClientIdentifier" />,
///         <see cref="ProtocolVersion" />) is unique across every row regardless of which device it
///         belongs to -- see this class's own <c>DnsClientAddressConfiguration</c> for the enforced
///         index -- since a single real identifier can only ever have been issued to one real
///         device. <c>IDnsClientRepository.GetByClientIdentifierAsync</c> looks a device up
///         by exactly this pair, resolving through this table to whichever <see cref="ClientId" />
///         currently owns it.
///     </para>
///     <para>
///         <strong><see cref="FirstSeenUtc" />/<see cref="LastSeenUtc" /></strong> exist so that a
///         presence-tracking feature built on top of this table can record when a sighting last
///         happened without needing a second migration to add the columns -- this entity's own
///         initial write path (client registration, or DHCP-driven auto-population) sets
///         <see cref="FirstSeenUtc" /> once and leaves <see cref="LastSeenUtc" /> unset until a
///         later sighting updates it.
///     </para>
/// </summary>
public class DnsClientAddressEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientEntity" /> (the real
    ///     device) this address belongs to.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>
    ///     This property contains the persistent identifier itself: a colon-separated hardware
    ///     (MAC) address for an <see cref="InternetProtocolVersionEnum.IPv4" /> identity, or a DHCP
    ///     Unique Identifier (DUID) for an <see cref="InternetProtocolVersionEnum.IPv6" /> one.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two disjoint identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to -- see
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.ClientIdentityResolution</c>'s own doc comment
    ///     for why a dual-stack device's IPv4 MAC address and IPv6 DUID are always two different
    ///     strings, never merged into one.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property contains the most recently observed source IP address this identifier
    ///     answered a query, ARP probe, or DHCP transaction from, or <see langword="null" /> when no
    ///     resolver has ever attributed a concrete address to this identifier yet. Purely
    ///     descriptive -- no code in this project resolves a client's identity <em>from</em> this
    ///     field; that resolution always flows the other way, through
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c>.
    /// </summary>
    public string Address { get; set; }

    /// <summary>
    ///     This property contains the UTC instant this identifier was first recorded against its
    ///     owning device -- set once, at creation, and never changed afterward.
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC instant this identifier was most recently observed --
    ///     <see langword="null" /> until a presence-tracking signal (an ARP sighting, a DHCP lease
    ///     grant/renewal, or live query activity) updates it. Online/offline status is always
    ///     inferred at read time by comparing this value against a configurable freshness window,
    ///     never stored as its own boolean, mirroring <see cref="DnsClientEntity.PausedUntilUtc" />'s
    ///     own self-correcting-at-read-time pattern.
    /// </summary>
    public DateTimeOffset? LastSeenUtc { get; set; }
}
