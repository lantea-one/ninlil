using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DHCP lease -- the record that a particular client currently
///     holds, or once held, a particular IP address. The same shape is shared by DHCPv4 (RFC 2131)
///     and DHCPv6 (RFC 8415) leases; <see cref="ProtocolVersion" /> says which protocol issued it,
///     and <see cref="ClientIdentifier" /> holds whichever identifier that protocol actually uses
///     (a MAC address for DHCPv4, a DUID for DHCPv6).
/// </summary>
public class LeaseEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property indicates whether this is a DHCPv4 or a DHCPv6 lease.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property contains the name of the DHCP scope (address pool) this lease was
    ///     handed out from -- the same value as the owning <see cref="DhcpScopeEntity.Name" />.
    /// </summary>
    public string ScopeName { get; set; }

    /// <summary>
    ///     This property contains the client's identifier: a MAC address for a DHCPv4 lease, or a
    ///     DHCP Unique Identifier (DUID) for a DHCPv6 lease.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property contains the leased IP address (an ordinary DHCPv4/DHCPv6 address
    ///     assignment), or, for a DHCPv6 Prefix Delegation lease
    ///     (<see cref="DhcpScopeEntity.Kind" /> of <c>PrefixDelegation</c>), the delegated prefix's
    ///     own base address -- <see cref="PrefixLength" /> says which of the two this row
    ///     represents.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains the delegated prefix's own length, in bits, for a DHCPv6 Prefix
    ///     Delegation lease -- <see langword="null" /> (the default) for an ordinary address
    ///     lease, which has no prefix length of its own.
    /// </summary>
    public int? PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the host name the client presented when requesting the lease,
    ///     if any.
    /// </summary>
    public string HostName { get; set; }

    /// <summary>
    ///     This property contains where the lease currently is in its lifecycle.
    /// </summary>
    public LeaseStateEnum State { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the lease was obtained by the client, or
    ///     <see langword="null" /> if it has only been offered so far.
    /// </summary>
    public DateTimeOffset? LeaseObtainedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the lease expires, or
    ///     <see langword="null" /> for a permanent reservation.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
}
