using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DHCP static reservation -- a fixed IP address permanently
///     assigned to one specific client within a <see cref="DhcpScopeEntity" />, identified by
///     whichever client identifier that scope's own <see cref="DhcpScopeEntity.ProtocolVersion" />
///     actually uses (a MAC address for DHCPv4, a DHCP Unique Identifier for DHCPv6). A
///     reservation exists independently of any lease ever having been granted for it; the
///     server consults it the moment a matching client is seen, before drawing from the scope's
///     own free-address pool at all.
/// </summary>
public class DhcpReservationEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DhcpScopeEntity" /> this
    ///     reservation belongs to.
    /// </summary>
    public Guid ScopeId { get; set; }

    /// <summary>
    ///     This property contains the client's identifier: a MAC address for a DHCPv4
    ///     reservation, or a DHCP Unique Identifier (DUID) for a DHCPv6 reservation.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property contains the fixed IP address reserved for this client.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains an operator-supplied host name for this reservation, if any --
    ///     independent of whatever host name a client itself later presents when it actually
    ///     leases the address.
    /// </summary>
    public string HostName { get; set; }

    /// <summary>
    ///     This property indicates whether this reservation is currently disabled. A disabled
    ///     reservation is kept on record but not honored -- the reserved address returns to the
    ///     scope's own ordinary free-address pool until the reservation is re-enabled.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this reservation.
    /// </summary>
    public string Comments { get; set; }
}
