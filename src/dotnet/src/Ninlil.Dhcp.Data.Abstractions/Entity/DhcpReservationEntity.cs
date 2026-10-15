using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a DHCP static reservation: a fixed IP address for one client within
///     a <see cref="DhcpScopeEntity" />, matched by the client identifier that the scope's
///     <see cref="DhcpScopeEntity.ProtocolVersion" /> uses (see <see cref="ClientIdentifier" />).
///     A reservation exists whether or not a lease has been granted; the lease allocation
///     services check it before picking a free address from the pool.
/// </summary>
public class DhcpReservationEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DhcpScopeEntity" /> this
    ///     reservation belongs to.
    /// </summary>
    public Guid ScopeId { get; set; }

    /// <summary>
    ///     This property contains the client's identifier: for DHCPv4 the MAC address (or the
    ///     Client-identifier option bytes, for a client that sends one), for DHCPv6 the DHCP
    ///     Unique Identifier (DUID). It is stored in the normal form of
    ///     <c>ClientIdentifierFormat</c> (lowercase, colon-separated hex, for example
    ///     <c>aa:bb:cc:dd:ee:ff</c>), which the reservations controller writes. The lease
    ///     allocation services look reservations up with
    ///     <see cref="Repository.IDhcpReservationRepository.FindByClientIdentifierAsync" />, an
    ///     exact string match against that form.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property contains the fixed IP address reserved for this client, in the canonical
    ///     text form <see cref="System.Net.IPAddress.ToString()" /> writes (for example
    ///     <c>2001:db8::10</c>), which the reservations controller stores.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains an operator-supplied host name for this reservation, if any,
    ///     separate from any host name the client presents when it leases the address.
    /// </summary>
    public string HostName { get; set; }

    /// <summary>
    ///     This property indicates whether this reservation is disabled. A disabled reservation
    ///     is kept but not honored, and its address can be handed out from the pool until it is
    ///     re-enabled.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this reservation.
    /// </summary>
    public string Comments { get; set; }
}
