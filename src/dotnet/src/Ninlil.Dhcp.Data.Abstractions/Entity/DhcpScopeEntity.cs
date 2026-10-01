using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a DHCP scope: a named pool the server hands out leases from, with
///     its lease times and the options sent with each lease. DHCPv4 scopes (IPv4 addresses) and
///     DHCPv6 scopes (RFC 8415 stateful addresses, or delegated prefixes when <see cref="Kind" />
///     is <see cref="DhcpScopeKindEnum.PrefixDelegation" />) share this shape;
///     <see cref="ProtocolVersion" /> says which one this is. The scope resolvers in
///     <c>Ninlil.Dhcp.Server.Core</c> pick the scope for a request, and
///     <c>DhcpScopesController</c> manages scopes.
/// </summary>
public class DhcpScopeEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the scope's operator-chosen, unique name. Leases refer to their
    ///     scope by this name (<see cref="LeaseEntity.ScopeName" />); reservations and options
    ///     refer to it by identifier (<see cref="DhcpReservationEntity.ScopeId" />).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property indicates whether this is a DHCPv4 or a DHCPv6 scope.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property indicates what this scope hands out (see <see cref="DhcpScopeKindEnum" />).
    ///     The default, <see cref="DhcpScopeKindEnum.AddressAssignment" />, is the only valid value
    ///     for a DHCPv4 scope.
    /// </summary>
    public DhcpScopeKindEnum Kind { get; set; } = DhcpScopeKindEnum.AddressAssignment;

    /// <summary>
    ///     This property contains the first address in the pool this scope hands leases out from
    ///     (<see cref="DhcpScopeKindEnum.AddressAssignment" />), or the first address of the
    ///     delegable prefix-block pool (<see cref="DhcpScopeKindEnum.PrefixDelegation" />), both
    ///     inclusive.
    /// </summary>
    public string RangeStartAddress { get; set; }

    /// <summary>
    ///     This property contains the last address in the pool this scope hands leases out from
    ///     (<see cref="DhcpScopeKindEnum.AddressAssignment" />), or the last address of the
    ///     delegable prefix-block pool (<see cref="DhcpScopeKindEnum.PrefixDelegation" />), both
    ///     inclusive.
    /// </summary>
    public string RangeEndAddress { get; set; }

    /// <summary>
    ///     This property contains the length, in bits, of each prefix this scope delegates (for
    ///     example <c>60</c> to delegate <c>/60</c>s out of a <c>/48</c> pool). It is required
    ///     when <see cref="Kind" /> is <see cref="DhcpScopeKindEnum.PrefixDelegation" /> and
    ///     <see langword="null" /> (the default) otherwise.
    /// </summary>
    public int? DelegatedPrefixLength { get; set; }

    /// <summary>
    ///     This property contains the CIDR prefix length of the network that
    ///     <see cref="RangeStartAddress" /> and <see cref="RangeEndAddress" /> belong to, for
    ///     example <c>24</c> for an IPv4 <c>/24</c> or <c>64</c> for the usual IPv6 <c>/64</c>.
    ///     For DHCPv4 it also produces the Subnet Mask option.
    /// </summary>
    public int SubnetPrefixLength { get; set; }

    /// <summary>
    ///     This property contains the lease duration granted to a client that does not request
    ///     one. The default is 24 hours.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property contains the longest lease duration this scope grants, even when a
    ///     client requests longer. The default is 7 days.
    /// </summary>
    public TimeSpan MaxLeaseDuration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     This property indicates whether this scope is disabled. A disabled scope keeps its
    ///     configuration and existing leases but offers no new leases.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the router (default gateway) addresses sent to every client, in
    ///     order of preference, as RFC 2132 Section 3.5's Router option (tag 3). Empty by default
    ///     (no gateway advertised). It applies to DHCPv4 only; IPv6 hosts learn their default
    ///     route from Router Advertisements (<c>Ninlil.Dhcp.Server.Ra</c>), not DHCPv6.
    /// </summary>
    public List<string> GatewayAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the DNS server addresses sent to every client, in order of
    ///     preference: RFC 2132 Section 3.8's Domain Name Server option (tag 6) for DHCPv4, the
    ///     RFC 3646 DNS Recursive Name Server option for DHCPv6. Empty by default (none sent).
    /// </summary>
    public List<string> DnsServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains free-form operator notes about this scope.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property contains the identifier of the DNS zone into which leases from this scope
    ///     register an A/AAAA record for the client's host name, or <see langword="null" /> (the
    ///     default) to turn DHCP-to-DNS registration off for this scope. Unlike
    ///     <c>ZoneEntity.AutoManageCaaRecords</c> and <c>AutoManagePtrRecords</c> there is no
    ///     global default, since the right zone depends on the scope's network.
    ///     <c>DhcpScopesController</c> checks only that the zone exists; whether its type is
    ///     eligible (Primary or CloudPrimary) is checked by
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>, as the CAA and PTR
    ///     features do in their services.
    /// </summary>
    public Guid? DnsZoneId { get; set; }

    /// <summary>
    ///     This property contains the boot file a PXE (or other BOOTP-aware) client of this DHCPv4
    ///     scope should request. It is sent in the RFC 2131 §2 "file" header field
    ///     (<c>Dhcpv4Message.BootFileName</c>) and, when the client sends no Parameter Request List
    ///     or its list asks for it, as RFC 2132 §9.5's Bootfile Name option (tag 67).
    ///     <see langword="null" /> (the default) offers no network boot.
    /// </summary>
    public string BootFileName { get; set; }

    /// <summary>
    ///     This property contains the address of the TFTP (or other boot) server a PXE client
    ///     should fetch <see cref="BootFileName" /> from. It is sent in the RFC 2131 §2 "siaddr"
    ///     header field (<c>Dhcpv4Message.ServerIpAddress</c>) and, under the same Parameter
    ///     Request List rule, as RFC 2132 §9.4's TFTP Server Name option (tag 66, in text form).
    ///     It is ignored when <see cref="BootFileName" /> is unset. <see langword="null" /> (the
    ///     default), or a value that does not parse, uses this server's Server Identifier address,
    ///     for the common case of a TFTP server on the same host.
    /// </summary>
    public string NextServerAddress { get; set; }
}
