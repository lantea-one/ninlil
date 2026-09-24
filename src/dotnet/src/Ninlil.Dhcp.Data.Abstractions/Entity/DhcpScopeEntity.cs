using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DHCP scope -- a named address pool a DHCP server hands
///     leases out from, along with the lease-timing rules that apply to every lease drawn from
///     it. The same shape is shared by a DHCPv4 scope (a pool of ordinary IPv4 addresses) and a
///     DHCPv6 scope (a pool of IPv6 addresses handed out via RFC 8415 stateful assignment);
///     <see cref="ProtocolVersion" /> says which protocol this scope serves.
/// </summary>
public class DhcpScopeEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the scope's own operator-chosen, unique name -- the same value
    ///     <see cref="LeaseEntity.ScopeName" /> and <see cref="DhcpReservationEntity.ScopeId" />
    ///     use to say which scope they belong to.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property indicates whether this is a DHCPv4 or a DHCPv6 scope.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property indicates what this scope actually hands out -- see
    ///     <see cref="DhcpScopeKindEnum" />'s own doc comment. Defaults to
    ///     <see cref="DhcpScopeKindEnum.AddressAssignment" />, the only value a DHCPv4 scope ever
    ///     legitimately has.
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
    ///     This property contains the length, in bits, of each individual prefix this scope
    ///     delegates to a client -- for example, <c>60</c> to delegate a <c>/60</c> out of a
    ///     <c>/48</c> pool. Meaningful, and required, only when <see cref="Kind" /> is
    ///     <see cref="DhcpScopeKindEnum.PrefixDelegation" />; <see langword="null" /> (the default)
    ///     for an <see cref="DhcpScopeKindEnum.AddressAssignment" /> scope, which has no delegated
    ///     prefix length of its own to speak of.
    /// </summary>
    public int? DelegatedPrefixLength { get; set; }

    /// <summary>
    ///     This property contains the CIDR prefix length, in bits, of the network
    ///     <see cref="RangeStartAddress" />/<see cref="RangeEndAddress" /> both belong to -- for
    ///     example, <c>24</c> for an ordinary IPv4 <c>/24</c> subnet, or <c>64</c> for the IPv6
    ///     <c>/64</c> a stateful DHCPv6 scope conventionally serves.
    /// </summary>
    public int SubnetPrefixLength { get; set; }

    /// <summary>
    ///     This property contains the lease duration granted to a client that does not itself
    ///     request a specific one.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property contains the longest lease duration this scope ever grants, even when a
    ///     client explicitly requests a longer one.
    /// </summary>
    public TimeSpan MaxLeaseDuration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     This property indicates whether this scope is currently disabled. A disabled scope
    ///     keeps its configuration, and any leases already granted from it, but stops offering new
    ///     leases.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the router (default gateway) addresses this scope hands out to
    ///     every client, in order of preference -- RFC 2132 Section 3.5's Router option (tag 3).
    ///     Empty by default (no gateway advertised). Only meaningful for a DHCPv4 scope; a stateful
    ///     DHCPv6 scope conveys its own default-route information via Router Advertisement instead,
    ///     a later sub-phase's own concern.
    /// </summary>
    public List<string> GatewayAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the DNS server addresses this scope hands out to every client,
    ///     in order of preference -- RFC 2132 Section 3.8's Domain Name Server option (tag 6).
    ///     Empty by default (no DNS server advertised).
    /// </summary>
    public List<string> DnsServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains free-form operator notes about this scope.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property contains the identifier of the DNS zone a lease drawn from this scope
    ///     should auto-register an A/AAAA record for its own presented host name into, or
    ///     <see langword="null" /> (the default) when this scope has DHCP-&gt;DNS integration
    ///     disabled. Unlike <c>ZoneEntity.AutoManageCaaRecords</c>/<c>AutoManagePtrRecords</c>,
    ///     there is no global default this falls back to: a designated zone is inherently
    ///     scope-specific (different scopes plausibly belong to entirely different networks/
    ///     domains), so there is no sensible zone-agnostic "on by default" value to inherit --
    ///     the feature is simply off for a scope until an operator names a real zone here.
    ///     Deliberately not validated for zone-type eligibility at write time
    ///     (<c>DhcpScopesController</c> only confirms the zone exists) -- the same
    ///     "enforced at the reconciliation-service level, not the REST layer" precedent
    ///     <c>ZoneEntity.AutoManageCaaRecords</c>/<c>AutoManagePtrRecords</c> already establish,
    ///     reused rather than reinvented for a third automated feature.
    /// </summary>
    public Guid? DnsZoneId { get; set; }

    /// <summary>
    ///     This property contains the name of the boot file a PXE (or any other BOOTP-aware)
    ///     client drawing a lease from this scope should request -- RFC 2131 §2's own "file"
    ///     header field (<c>Dhcpv4Message.BootFileName</c>), and, when the client's own Parameter
    ///     Request List asks for it, also delivered as RFC 2132 §9.5's Bootfile Name option (tag
    ///     67). <see langword="null" /> (the default) leaves this scope's own leases with no boot
    ///     file at all -- ordinary DHCP behavior, no PXE netboot offered.
    /// </summary>
    public string BootFileName { get; set; }

    /// <summary>
    ///     This property contains the address of the TFTP (or other boot) server a PXE client
    ///     drawing a lease from this scope should contact for <see cref="BootFileName" /> -- RFC
    ///     2131 §2's own "siaddr" header field (<c>Dhcpv4Message.ServerIpAddress</c>), and, when
    ///     requested, also delivered as RFC 2132 §9.4's TFTP Server Name option (tag 66).
    ///     Meaningless, and never applied, when <see cref="BootFileName" /> is unset.
    ///     <see langword="null" /> (the default) falls back to this server's own Server Identifier
    ///     address -- the common case of a homelab boot/TFTP server colocated with the DHCP
    ///     server itself, the same default dnsmasq's own <c>dhcp-boot</c> directive assumes when
    ///     no explicit server address is given.
    /// </summary>
    public string NextServerAddress { get; set; }
}
