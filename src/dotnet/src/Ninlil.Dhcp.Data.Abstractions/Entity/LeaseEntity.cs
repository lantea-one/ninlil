using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a DHCP lease: the record that a client holds, was offered, or once
///     held an address (or, for DHCPv6 prefix delegation, a prefix). DHCPv4 (RFC 2131) and DHCPv6
///     (RFC 8415) leases share this shape; <see cref="ProtocolVersion" /> says which protocol
///     issued it. The lease allocation services in <c>Ninlil.Dhcp.Server.Core</c> create and
///     update leases; an expired or released row stays as a record.
/// </summary>
public class LeaseEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property indicates whether this is a DHCPv4 or a DHCPv6 lease.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property contains the name of the scope this lease came from
    ///     (<see cref="DhcpScopeEntity.Name" />).
    /// </summary>
    public string ScopeName { get; set; }

    /// <summary>
    ///     This property contains the client's identifier, in the format described on
    ///     <see cref="DhcpReservationEntity.ClientIdentifier" />: the MAC address or
    ///     Client-identifier for DHCPv4, the DUID for DHCPv6.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property contains the leased address or, for a DHCPv6 prefix delegation lease
    ///     (<see cref="DhcpScopeEntity.Kind" /> of <c>PrefixDelegation</c>), the delegated prefix's
    ///     base address. <see cref="PrefixLength" /> tells the two apart.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains the delegated prefix length, in bits, for a DHCPv6 prefix
    ///     delegation lease, and <see langword="null" /> (the default) for an address lease.
    /// </summary>
    public int? PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the host name the client presented when requesting the lease,
    ///     if any.
    /// </summary>
    public string HostName { get; set; }

    /// <summary>
    ///     This property contains where the lease is in its lifecycle (offered, active, expired or
    ///     released; a declined address is held as expired).
    /// </summary>
    public LeaseStateEnum State { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the client first obtained the lease, or
    ///     <see langword="null" /> if it has only been offered.
    /// </summary>
    public DateTimeOffset? LeaseObtainedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the current state ends: the end of the
    ///     offer hold, the lease, or a declined address's quarantine. The allocation services
    ///     always set it.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
}
