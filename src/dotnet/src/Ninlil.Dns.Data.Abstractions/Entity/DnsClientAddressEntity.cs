using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one persistent identifier a device (<see cref="DnsClientEntity" />) is
///     known by: a colon-separated MAC address for an <see cref="InternetProtocolVersionEnum.IPv4" />
///     identity, or a DHCP Unique Identifier (DUID) for an <see cref="InternetProtocolVersionEnum.IPv6" />
///     one. A device has many of these rather than one identifier of its own, so a dual-stack
///     device with an IPv4 MAC address (seen over ARP or DHCPv4) and a different IPv6 DUID (seen
///     over DHCPv6) is one <see cref="DnsClientEntity" /> with two addresses.
///     <para>
///         <strong>Uniqueness</strong>: the pair (<see cref="ClientIdentifier" />,
///         <see cref="ProtocolVersion" />) is unique across all rows, whatever the device
///         (<c>DnsClientAddressConfiguration</c> defines the index), because an identifier belongs
///         to one device. <c>IDnsClientRepository.GetByClientIdentifierAsync</c> looks a device up
///         by this pair through this table.
///     </para>
///     <para>
///         <strong>Sightings</strong>: <see cref="FirstSeenUtc" /> is set once when the row is
///         created (by registration or DHCP auto-population). <see cref="LastSeenUtc" /> stays
///         unset until a later sighting; <c>ClientPresenceFlushService</c> updates it.
///     </para>
/// </summary>
public class DnsClientAddressEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientEntity" /> (the device)
    ///     this address belongs to.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>
    ///     This property contains the persistent identifier: a colon-separated MAC address for an
    ///     <see cref="InternetProtocolVersionEnum.IPv4" /> identity, or a DUID for an
    ///     <see cref="InternetProtocolVersionEnum.IPv6" /> one.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two separate identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to. See
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.ClientIdentityResolution</c> for why a dual-stack
    ///     device's MAC address and DUID are kept as two identifiers.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property contains the most recent source IP address seen for this identifier (in a
    ///     query, an ARP probe or a DHCP transaction), or <see langword="null" /> if none has been
    ///     seen. It is descriptive only: identity is never resolved from it, only the other way,
    ///     through <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c>.
    /// </summary>
    public string Address { get; set; }

    /// <summary>
    ///     This property contains the UTC time this identifier was first recorded for its device.
    ///     It is set at creation and never changed.
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC time this identifier was last seen, or
    ///     <see langword="null" /> until a sighting (ARP, a DHCP lease grant or renewal, or a query)
    ///     records one. Online status is worked out when read, by comparing this with a configurable
    ///     window, and is never stored; <see cref="DnsClientEntity.PausedUntilUtc" /> works the same
    ///     way.
    /// </summary>
    public DateTimeOffset? LastSeenUtc { get; set; }
}
