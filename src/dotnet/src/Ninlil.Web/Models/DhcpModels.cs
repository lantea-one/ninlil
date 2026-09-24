using System.Text.Json.Serialization;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a DHCP scope
///     (<c>POST</c>/<c>PUT /api/dhcp-scopes</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this scope.</param>
/// <param name="ProtocolVersion">Whether this is a DHCPv4 or a DHCPv6 scope.</param>
/// <param name="Kind">
///     What this scope actually hands out -- ordinary address assignment, or (DHCPv6 only) RFC
///     8415 Prefix Delegation. Defaults to <see cref="DhcpScopeKindEnum.AddressAssignment" />, the
///     only value a DHCPv4 scope ever legitimately has -- see
///     <c>DhcpScopeEntity.Kind</c>'s own doc comment.
/// </param>
/// <param name="RangeStartAddress">The first address in the pool this scope hands leases out from, inclusive.</param>
/// <param name="RangeEndAddress">The last address in the pool this scope hands leases out from, inclusive.</param>
/// <param name="SubnetPrefixLength">The CIDR prefix length, in bits, of the network the range belongs to.</param>
/// <param name="DefaultLeaseDuration">The lease duration granted to a client that does not request a specific one.</param>
/// <param name="MaxLeaseDuration">The longest lease duration this scope ever grants.</param>
/// <param name="IsDisabled">Whether the scope starts out (or is currently) disabled.</param>
/// <param name="GatewayAddresses">The router (default gateway) addresses this scope hands out, in order of preference.</param>
/// <param name="DnsServerAddresses">The DNS server addresses this scope hands out, in order of preference.</param>
/// <param name="Comments">Free-form operator notes about this scope.</param>
/// <param name="DnsZoneId">
///     The identifier of the DNS zone a lease drawn from this scope should auto-register an
///     A/AAAA record for its own presented host name into, or <see langword="null" /> to leave
///     DHCP-&gt;DNS integration disabled for this scope.
/// </param>
/// <param name="BootFileName">
///     The name of the boot file a PXE client drawing a lease from this scope should request, or
///     <see langword="null" /> to offer no boot file at all -- see
///     <c>DhcpScopeEntity.BootFileName</c>'s own doc comment.
/// </param>
/// <param name="NextServerAddress">
///     The address of the TFTP (or other boot) server a PXE client should contact for
///     <paramref name="BootFileName" />, or <see langword="null" /> to fall back to this server's
///     own Server Identifier address -- see <c>DhcpScopeEntity.NextServerAddress</c>'s own doc
///     comment.
/// </param>
/// <param name="DelegatedPrefixLength">
///     The length, in bits, of each individual prefix this scope delegates to a client -- required
///     when <paramref name="Kind" /> is <see cref="DhcpScopeKindEnum.PrefixDelegation" />, and
///     rejected (must be <see langword="null" />) otherwise -- see
///     <c>DhcpScopeEntity.DelegatedPrefixLength</c>'s own doc comment.
/// </param>
public record DhcpScopeWriteRequest(
    string Name,
    InternetProtocolVersionEnum ProtocolVersion,
    string RangeStartAddress,
    string RangeEndAddress,
    int SubnetPrefixLength,
    TimeSpan DefaultLeaseDuration,
    TimeSpan MaxLeaseDuration,
    bool IsDisabled = false,
    List<string> GatewayAddresses = null,
    List<string> DnsServerAddresses = null,
    string Comments = null,
    Guid? DnsZoneId = null,
    string BootFileName = null,
    string NextServerAddress = null,
    DhcpScopeKindEnum Kind = DhcpScopeKindEnum.AddressAssignment,
    int? DelegatedPrefixLength = null
);

/// <summary>
///     This class carries a DHCP scope's own metadata, for list, single-item, and creation
///     responses.
/// </summary>
public record DhcpScopeDetail(
    Guid Id,
    string Name,
    InternetProtocolVersionEnum ProtocolVersion,
    string RangeStartAddress,
    string RangeEndAddress,
    int SubnetPrefixLength,
    TimeSpan DefaultLeaseDuration,
    TimeSpan MaxLeaseDuration,
    bool IsDisabled,
    IReadOnlyList<string> GatewayAddresses,
    IReadOnlyList<string> DnsServerAddresses,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? DnsZoneId,
    string BootFileName,
    string NextServerAddress,
    DhcpScopeKindEnum Kind,
    int? DelegatedPrefixLength
);

/// <summary>
///     This record is the REST representation of <c>Ninlil.Dhcp.Server.Core.Statistics.DhcpScopeStatistics</c>
///     -- a single DHCP scope's own genuine, freshly-computed utilization, never a cached or
///     approximate figure (see that class's own doc comment for exactly how each field is
///     derived). <see cref="TotalSlots" />/<see cref="AvailableSlots" /> are carried as a decimal
///     string, not a JSON number: an IPv6 <c>AddressAssignment</c> scope's own configured range can
///     legitimately hold far more addresses than fit in a JSON number's own safe-integer range (an
///     ordinary <c>/64</c> alone holds 2^64 of them) without losing precision.
/// </summary>
/// <param name="ScopeId">The identifier of the scope these figures describe.</param>
/// <param name="ScopeName">The name of the scope these figures describe.</param>
/// <param name="ProtocolVersion">Whether this is a DHCPv4 or a DHCPv6 scope.</param>
/// <param name="Kind">What this scope actually hands out -- see <c>DhcpScopeEntity.Kind</c>'s own doc comment.</param>
/// <param name="TotalSlots">
///     The total number of slots the scope's own configured range holds, as a decimal string -- an
///     ordinary address for an <see cref="DhcpScopeKindEnum.AddressAssignment" /> scope, or a
///     block-aligned delegable prefix for a <see cref="DhcpScopeKindEnum.PrefixDelegation" /> one.
/// </param>
/// <param name="LeasedCount">The number of currently actively-leased slots.</param>
/// <param name="OfferedCount">The number of slots currently tentatively offered, awaiting confirmation.</param>
/// <param name="ReservedCount">The number of this scope's own currently-enabled static reservations.</param>
/// <param name="AvailableSlots">
///     <paramref name="TotalSlots" /> minus every distinct slot currently occupied by a blocking
///     lease or an enabled reservation, as a decimal string -- never negative.
/// </param>
public record DhcpScopeStatisticsDetail(
    Guid ScopeId,
    string ScopeName,
    InternetProtocolVersionEnum ProtocolVersion,
    DhcpScopeKindEnum Kind,
    string TotalSlots,
    int LeasedCount,
    int OfferedCount,
    int ReservedCount,
    string AvailableSlots
);

/// <summary>
///     This class carries the fields used to create or fully replace a static reservation
///     belonging to a DHCP scope (<c>POST</c>/<c>PUT /api/dhcp-scopes/{scopeId}/reservations</c>).
/// </summary>
/// <param name="ClientIdentifier">The client's identifier: a MAC address for DHCPv4, or a DUID for DHCPv6.</param>
/// <param name="IpAddress">The fixed IP address reserved for this client.</param>
/// <param name="HostName">An operator-supplied host name for this reservation, if any.</param>
/// <param name="IsDisabled">Whether the reservation starts out (or is currently) disabled.</param>
/// <param name="Comments">Free-form operator notes about this reservation.</param>
public record DhcpReservationWriteRequest(
    string ClientIdentifier,
    string IpAddress,
    string HostName = null,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a DHCP reservation's own fields, for list, single-item, and creation
///     responses.
/// </summary>
public record DhcpReservationDetail(
    Guid Id,
    Guid ScopeId,
    string ClientIdentifier,
    string IpAddress,
    string HostName,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries the fields used to create or fully replace a custom, raw DHCPv4 option
///     belonging to a scope (<c>POST</c>/<c>PUT /api/dhcp-scopes/{scopeId}/custom-options</c>).
/// </summary>
/// <param name="OptionTag">The option's own raw numeric tag (0-255).</param>
/// <param name="ValueHex">
///     The option's own raw value bytes, as an uppercase hexadecimal string -- ignored entirely at
///     delivery time (and may be omitted/left blank) when <paramref name="ValueSource" /> is
///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
/// </param>
/// <param name="IsDisabled">Whether the custom option starts out (or is currently) disabled.</param>
/// <param name="ValueSource">
///     Where this option's own delivered value actually comes from -- see
///     <see cref="DhcpOptionValueSourceEnum" />'s own doc comment. Defaults to
///     <see cref="DhcpOptionValueSourceEnum.Custom" />, matching every write request from before
///     this field existed.
/// </param>
/// <param name="Comments">Free-form operator notes about this custom option.</param>
public record DhcpScopeCustomOptionWriteRequest(
    byte OptionTag,
    string ValueHex,
    bool IsDisabled = false,
    DhcpOptionValueSourceEnum ValueSource = DhcpOptionValueSourceEnum.Custom,
    string Comments = null
);

/// <summary>
///     This class carries a scope's own custom option fields, for list, single-item, and creation
///     responses.
/// </summary>
public record DhcpScopeCustomOptionDetail(
    Guid Id,
    Guid ScopeId,
    byte OptionTag,
    string ValueHex,
    bool IsDisabled,
    DhcpOptionValueSourceEnum ValueSource,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries the fields used to create or fully replace a tag-based conditional
///     option delivery rule (<c>POST</c>/<c>PUT /api/dhcp-tag-rules</c>). See
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity</c>'s own doc comment for the
///     full match/priority/conflict-resolution semantics every field here drives.
/// </summary>
/// <param name="Name">An operator-chosen, descriptive name for this rule.</param>
/// <param name="ScopeId">
///     Optionally restricts this rule to one specific scope; <see langword="null" /> makes it
///     eligible against a client resolved into any scope.
/// </param>
/// <param name="Priority">This rule's own evaluation order, ascending.</param>
/// <param name="IsDisabled">Whether the rule starts out (or is currently) disabled.</param>
/// <param name="VendorClassIdentifierMatch">The Vendor Class Identifier value this rule matches against, if any.</param>
/// <param name="VendorClassIdentifierMatchMode">How <paramref name="VendorClassIdentifierMatch" /> is compared.</param>
/// <param name="UserClassMatch">The User Class value this rule matches against, if any.</param>
/// <param name="UserClassMatchMode">How <paramref name="UserClassMatch" /> is compared.</param>
/// <param name="RelayAgentCircuitIdHex">The Agent Circuit ID value this rule requires, as hex, if any (DHCPv4 only).</param>
/// <param name="RelayAgentRemoteIdHex">The Agent/DHCPv6 Remote ID value this rule requires, as hex, if any.</param>
/// <param name="RelayInterfaceIdHex">The DHCPv6 Interface-ID value this rule requires, as hex, if any (DHCPv6 only).</param>
/// <param name="VendorClassEnterpriseNumber">The IANA Enterprise Number a DHCPv6 Vendor Class option must carry, if any (DHCPv6 only).</param>
/// <param name="Comments">Free-form operator notes about this rule.</param>
public record DhcpTagRuleWriteRequest(
    string Name,
    Guid? ScopeId,
    int Priority = 0,
    bool IsDisabled = false,
    string VendorClassIdentifierMatch = null,
    Dhcpv4TagMatchModeEnum VendorClassIdentifierMatchMode = Dhcpv4TagMatchModeEnum.Exact,
    string UserClassMatch = null,
    Dhcpv4TagMatchModeEnum UserClassMatchMode = Dhcpv4TagMatchModeEnum.Exact,
    string RelayAgentCircuitIdHex = null,
    string RelayAgentRemoteIdHex = null,
    string RelayInterfaceIdHex = null,
    uint? VendorClassEnterpriseNumber = null,
    string Comments = null
);

/// <summary>
///     This class carries a tag rule's own metadata, for list, single-item, and creation
///     responses.
/// </summary>
public record DhcpTagRuleDetail(
    Guid Id,
    string Name,
    Guid? ScopeId,
    int Priority,
    bool IsDisabled,
    string VendorClassIdentifierMatch,
    Dhcpv4TagMatchModeEnum VendorClassIdentifierMatchMode,
    string UserClassMatch,
    Dhcpv4TagMatchModeEnum UserClassMatchMode,
    string RelayAgentCircuitIdHex,
    string RelayAgentRemoteIdHex,
    string RelayInterfaceIdHex,
    uint? VendorClassEnterpriseNumber,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries the fields used to create or fully replace an option a tag rule
///     contributes (<c>POST</c>/<c>PUT /api/dhcp-tag-rules/{tagRuleId}/options</c>).
/// </summary>
/// <param name="OptionTag">The option's own raw numeric tag (0-255).</param>
/// <param name="ValueHex">
///     The option's own raw value bytes, as an uppercase hexadecimal string -- ignored entirely at
///     delivery time (and may be omitted/left blank) when <paramref name="ValueSource" /> is
///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
/// </param>
/// <param name="ValueSource">
///     Where this option's own delivered value actually comes from -- the same meaning
///     <see cref="DhcpScopeCustomOptionWriteRequest.ValueSource" /> already documents.
/// </param>
public record DhcpTagRuleOptionWriteRequest(
    byte OptionTag,
    string ValueHex,
    DhcpOptionValueSourceEnum ValueSource = DhcpOptionValueSourceEnum.Custom
);

/// <summary>
///     This class carries a tag rule option's own fields, for list, single-item, and creation
///     responses.
/// </summary>
public record DhcpTagRuleOptionDetail(
    Guid Id,
    Guid TagRuleId,
    byte OptionTag,
    string ValueHex,
    DhcpOptionValueSourceEnum ValueSource,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
///     This class carries a single DHCP lease's own fields, for list and single-item responses.
///     There is deliberately no corresponding write request -- a lease is a live artifact the
///     allocation engine itself creates and updates as clients come and go, never an
///     operator-created resource. <see cref="PrefixLength" /> is populated only for a DHCPv6
///     Prefix Delegation lease -- <see langword="null" /> for every ordinary address lease, per
///     <c>LeaseEntity.PrefixLength</c>'s own doc comment.
/// </summary>
public record DhcpLeaseDetail(
    Guid Id,
    InternetProtocolVersionEnum ProtocolVersion,
    string ScopeName,
    string ClientIdentifier,
    string IpAddress,
    string HostName,
    LeaseStateEnum State,
    DateTimeOffset? LeaseObtainedAtUtc,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int? PrefixLength
);

/// <summary>
///     This record is the REST representation of <c>Ninlil.Dhcp.Server.Core.DhcpServerOptions</c>
///     -- the settings shared by every local DHCP protocol engine.
/// </summary>
/// <param name="ServerIdentifierAddress">
///     The IPv4 address this server identifies itself as, or <see langword="null" /> for
///     auto-detection at startup.
/// </param>
/// <param name="OfferHoldDuration">How long a tentatively offered address is held aside before being reclaimed.</param>
/// <param name="DeclinedAddressQuarantineDuration">How long a client-declined address is kept out of circulation.</param>
public record DhcpCoreSettingsDetail(
    string ServerIdentifierAddress,
    TimeSpan OfferHoldDuration,
    TimeSpan DeclinedAddressQuarantineDuration
);

/// <summary>
///     This record is the REST representation of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions</c> -- the DHCPv4 UDP transport's own
///     bind/port settings.
/// </summary>
/// <param name="Enable">Whether this listener binds its socket and answers DHCPv4 traffic at all.</param>
/// <param name="ServerPort">The port this listener binds to.</param>
/// <param name="ClientPort">The port a reply sent directly to a client is sent to.</param>
public record DhcpProtocolSettingsDetail(bool Enable, int ServerPort, int ClientPort);

/// <summary>
///     This record is the REST representation of <c>Ninlil.Dhcp.Server.Relay.Dhcpv4RelayOptions</c>
///     -- whether and how this server acts as a DHCPv4 relay agent instead of answering requests
///     itself from local scope/lease state.
/// </summary>
/// <param name="Enable">Whether this server acts as a DHCPv4 relay agent at all.</param>
/// <param name="UpstreamServerAddresses">The IPv4 addresses of every upstream DHCPv4 server this relay forwards to.</param>
/// <param name="UpstreamServerPort">The UDP port every configured upstream DHCPv4 server listens on.</param>
/// <param name="RelayAgentAddress">
///     The IPv4 address this relay identifies itself as via <c>giaddr</c>, or
///     <see langword="null" /> for auto-detection at startup.
/// </param>
/// <param name="RelayAgentCircuitId">The Agent Circuit ID text this relay adds to every forwarded request, if any.</param>
/// <param name="RelayAgentRemoteId">The Agent Remote ID text this relay adds to every forwarded request, if any.</param>
public record DhcpRelaySettingsDetail(
    bool Enable,
    IReadOnlyList<string> UpstreamServerAddresses,
    int UpstreamServerPort,
    string RelayAgentAddress,
    string RelayAgentCircuitId,
    string RelayAgentRemoteId
);

/// <summary>
///     This record is the REST representation of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ProtocolOptions</c> -- the DHCPv6 UDP transport's own
///     bind/port settings, the DHCPv6 analogue of <see cref="DhcpProtocolSettingsDetail" />.
/// </summary>
/// <param name="Enable">Whether this listener binds its socket and answers DHCPv6 traffic at all.</param>
/// <param name="ServerPort">The port this listener binds to -- 547 is the IANA-assigned DHCPv6 server port.</param>
/// <param name="ClientPort">The port a reply is unicast back to -- 546 is the IANA-assigned DHCPv6 client port.</param>
public record Dhcpv6ProtocolSettingsDetail(bool Enable, int ServerPort, int ClientPort);

/// <summary>
///     This record is the REST representation of
///     <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayOptions</c> -- whether and how this server acts as
///     an RFC 8415 DHCPv6 relay agent instead of answering requests itself from local scope/lease
///     state, the DHCPv6 analogue of <see cref="DhcpRelaySettingsDetail" />.
/// </summary>
/// <param name="Enable">Whether this server acts as a DHCPv6 relay agent at all.</param>
/// <param name="UpstreamServerAddresses">The IPv6 addresses of every upstream DHCPv6 server this relay forwards a RELAY-FORW envelope to.</param>
/// <param name="UpstreamServerPort">The UDP port every configured upstream DHCPv6 server listens on -- 547 is the IANA-assigned DHCPv6 server port.</param>
/// <param name="RelayLinkAddress">
///     The IPv6 address this relay identifies itself with via RELAY-FORW's own "link-address"
///     field, or <see langword="null" /> for auto-detection at startup.
/// </param>
/// <param name="InterfaceId">The RFC 8415 Section 21.18 Interface-ID text this relay adds to every RELAY-FORW it sends, if any.</param>
public record Dhcpv6RelaySettingsDetail(
    bool Enable,
    IReadOnlyList<string> UpstreamServerAddresses,
    int UpstreamServerPort,
    string RelayLinkAddress,
    string InterfaceId
);

/// <summary>
///     This record is the REST representation of
///     <c>Ninlil.Dhcp.Server.Tftp.TftpServerOptions</c> -- this server's own bundled, read-only
///     RFC 1350 TFTP server.
/// </summary>
/// <param name="Enable">Whether this server binds its socket and answers TFTP requests at all.</param>
/// <param name="Port">The UDP port this server listens for incoming Read/Write Requests on.</param>
/// <param name="RootDirectory">The directory this server serves files from.</param>
/// <param name="RetransmitTimeoutSeconds">How long, in seconds, this server waits for an Acknowledgment before retransmitting a block.</param>
/// <param name="MaxRetransmissions">The greatest number of times this server retransmits one block before giving up on a transfer.</param>
public record DhcpTftpSettingsDetail(
    bool Enable,
    int Port,
    string RootDirectory,
    double RetransmitTimeoutSeconds,
    int MaxRetransmissions
);

/// <summary>
///     This record is the REST representation of
///     <c>Ninlil.Dhcp.Protocol.Ra.RaProtocolOptions</c> -- how this server's own Router
///     Advertisement listener binds and times its unsolicited advertisements. What a Router
///     Advertisement actually contains is <see cref="DhcpRaSettingsDetail" />'s own concern
///     instead, mirroring that same split in the underlying <c>Options</c> classes.
/// </summary>
/// <param name="Enable">Whether this listener binds its raw ICMPv6 socket and sends/answers Router Advertisement traffic at all.</param>
/// <param name="MinRouterAdvertisementInterval">The shortest interval between two unsolicited advertisements on the same interface.</param>
/// <param name="MaxRouterAdvertisementInterval">The longest interval between two unsolicited advertisements on the same interface.</param>
public record DhcpRaProtocolSettingsDetail(
    bool Enable,
    TimeSpan MinRouterAdvertisementInterval,
    TimeSpan MaxRouterAdvertisementInterval
);

/// <summary>
///     This record is the REST representation of the scalar/list fields of
///     <c>Ninlil.Dhcp.Server.Ra.RaOptions</c> -- what a Router Advertisement this server sends
///     actually contains, the content counterpart to <see cref="DhcpRaProtocolSettingsDetail" />'s
///     own transport/timing settings.
///     <para>
///         <b>Deliberately excludes <c>RaOptions.Prefixes</c></b>: each prefix carries several
///         independently-flagged/lifetimed fields (on-link/autonomous flags, two separate
///         lifetimes) that together give it a shape closer to a genuinely independent, addressable
///         resource -- configuring advertised prefixes remains config-file-only for now, a
///         genuine, deliberate scope boundary rather than an oversight, left for a future
///         dedicated sub-resource (the same shape <c>DhcpReservationsController</c> already gives
///         a scope's own reservations).
///     </para>
///     <para>
///         <b><see cref="Routes" /> is deliberately handled differently</b>, and does get real
///         REST/UI coverage here: an RFC 4191 route is a simpler, self-contained triple
///         (prefix/prefix length, preference, lifetime) with no independent flags of its own, so it
///         fits this controller's existing whole-array-replace shape -- the same one
///         <see cref="RecursiveDnsServers" />/<see cref="DnsSearchListDomains" /> already use --
///         rather than needing a genuinely separate, identity-bearing sub-resource the way a
///         database-backed entity (a DHCP reservation, a scope's own custom option) does.
///         <c>RaOptions</c> itself has no database backing at all -- every field here, this one
///         included, lives in the JSON configuration file and is restart-required -- so a
///         sub-resource pattern built around a persisted entity's own stable identifier does not
///         actually fit the way this data is stored; a <c>PUT</c>-style whole-list replace on save
///         does.
///     </para>
/// </summary>
/// <param name="ManagedAddressConfiguration">RFC 4861's own "M" flag -- hosts should obtain addresses via DHCPv6 stateful assignment.</param>
/// <param name="OtherConfiguration">RFC 4861's own "O" flag -- hosts should still use DHCPv6 for configuration other than the address itself.</param>
/// <param name="RouterLifetime">How long a host may consider this server usable as its own default router; zero means never.</param>
/// <param name="CurrentHopLimit">The hop limit value recommended for outgoing unicast packets; zero means no recommendation.</param>
/// <param name="ReachableTime">How long an address is assumed reachable absent contrary evidence; zero means no recommendation.</param>
/// <param name="RetransTimer">How long a host should wait before retransmitting a Neighbor Solicitation; zero means no recommendation.</param>
/// <param name="LinkMtu">The link's own MTU, or <see langword="null" /> to advertise no MTU option at all.</param>
/// <param name="RecursiveDnsServers">The Recursive DNS Server (RFC 8106 Section 5.1) addresses advertised to a SLAAC-configured host.</param>
/// <param name="RecursiveDnsServerLifetime">How long a host should trust <paramref name="RecursiveDnsServers" />.</param>
/// <param name="DnsSearchListDomains">The DNS Search List (RFC 8106 Section 5.2) domain suffixes advertised to a SLAAC-configured host.</param>
/// <param name="DnsSearchListLifetime">How long a host should trust <paramref name="DnsSearchListDomains" />.</param>
/// <param name="Routes">Every specific route advertised via RFC 4191 Section 2.3's own Route Information option.</param>
/// <param name="InterfaceOverrides">
///     Every network interface's own explicit content override -- see
///     <see cref="DhcpRaInterfaceOverrideDetail" />'s own doc comment for the exact "nullable field
///     inherits the fields above" shape and for why, unlike <see cref="Routes" />, an override's
///     own would-be nested prefix/route lists have no REST/UI surface here.
/// </param>
public record DhcpRaSettingsDetail(
    bool ManagedAddressConfiguration,
    bool OtherConfiguration,
    TimeSpan RouterLifetime,
    int CurrentHopLimit,
    TimeSpan ReachableTime,
    TimeSpan RetransTimer,
    uint? LinkMtu,
    IReadOnlyList<string> RecursiveDnsServers,
    TimeSpan RecursiveDnsServerLifetime,
    IReadOnlyList<string> DnsSearchListDomains,
    TimeSpan DnsSearchListLifetime,
    IReadOnlyList<DhcpRaRouteDetail> Routes,
    IReadOnlyList<DhcpRaInterfaceOverrideDetail> InterfaceOverrides
);

/// <summary>
///     This record is the REST representation of one <c>Ninlil.Dhcp.Server.Ra.RaAdvertisedRouteOptions</c>
///     entry -- one specific route advertised via RFC 4191 Section 2.3's own Route Information
///     option, nested inside <see cref="DhcpRaSettingsDetail" />/the <c>PATCH ra</c> merge-patch
///     body's own <c>routes</c> array.
/// </summary>
/// <param name="Prefix">The route's own prefix, or <see langword="null" />/empty when <paramref name="PrefixLength" /> is <c>0</c> (a default-route-preference-only entry).</param>
/// <param name="PrefixLength">The number of leading significant bits of <paramref name="Prefix" />, <c>0</c> through <c>128</c>.</param>
/// <param name="Preference">This route's own preference (RFC 4191 Section 2.1) relative to any other route or default router known on the link.</param>
/// <param name="RouteLifetime">How long this route remains valid, from the moment each advertisement carrying it is sent.</param>
public record DhcpRaRouteDetail(
    string Prefix,
    int PrefixLength,
    RaRoutePreferenceEnum Preference,
    TimeSpan RouteLifetime
);

/// <summary>
///     This record is the REST representation of one <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c>
///     entry -- one network interface's own distinct Router Advertisement content, nested inside
///     <see cref="DhcpRaSettingsDetail" />/the <c>PATCH ra</c> merge-patch body's own
///     <c>interfaceOverrides</c> array. Every scalar field below is nullable and, left
///     <see langword="null" />, inherits <see cref="DhcpRaSettingsDetail" />'s own matching global
///     field verbatim for this one interface -- the same shape
///     <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c>'s own doc comment describes in full,
///     including <see cref="RecursiveDnsServers" />/<see cref="DnsSearchListDomains" />'s own
///     "null inherits the global list, non-null (even empty) wholesale-replaces it" list-level
///     rule.
///     <para>
///         <b>Deliberately excludes an override's own would-be <c>Prefixes</c>/<c>Routes</c>
///         lists</b>, exactly mirroring <see cref="DhcpRaSettingsDetail" />'s own established
///         "<c>Prefixes</c> stays config-file-only" scope boundary -- extended here to
///         <c>Routes</c> as well, since nesting a repeatable route list inside this already-
///         repeatable interface-override list is a materially bigger REST/UI lift than everything
///         else this record carries. Both remain fully configurable directly in the JSON
///         configuration file (<c>RaInterfaceOverrideOptions.Prefixes</c>/<c>.Routes</c>), a
///         genuine, deliberate scope cut rather than an oversight.
///     </para>
/// </summary>
/// <param name="InterfaceName">The OS-reported network interface name this override applies to (for example <c>eth0</c>), matched case-insensitively.</param>
/// <param name="Exclude">When <see langword="true" />, this interface is excluded from Router Advertisement entirely; every field below is then ignored.</param>
/// <param name="ManagedAddressConfiguration">Overrides <see cref="DhcpRaSettingsDetail.ManagedAddressConfiguration" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="OtherConfiguration">Overrides <see cref="DhcpRaSettingsDetail.OtherConfiguration" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="RouterLifetime">Overrides <see cref="DhcpRaSettingsDetail.RouterLifetime" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="CurrentHopLimit">Overrides <see cref="DhcpRaSettingsDetail.CurrentHopLimit" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="ReachableTime">Overrides <see cref="DhcpRaSettingsDetail.ReachableTime" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="RetransTimer">Overrides <see cref="DhcpRaSettingsDetail.RetransTimer" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="LinkMtu">Overrides <see cref="DhcpRaSettingsDetail.LinkMtu" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="RecursiveDnsServers">Wholesale-replaces <see cref="DhcpRaSettingsDetail.RecursiveDnsServers" /> for this interface when non-<see langword="null" />, or inherits the global list when <see langword="null" />.</param>
/// <param name="RecursiveDnsServerLifetime">Overrides <see cref="DhcpRaSettingsDetail.RecursiveDnsServerLifetime" /> for this interface, or inherits it when <see langword="null" />.</param>
/// <param name="DnsSearchListDomains">Wholesale-replaces <see cref="DhcpRaSettingsDetail.DnsSearchListDomains" /> for this interface when non-<see langword="null" />, or inherits the global list when <see langword="null" />.</param>
/// <param name="DnsSearchListLifetime">Overrides <see cref="DhcpRaSettingsDetail.DnsSearchListLifetime" /> for this interface, or inherits it when <see langword="null" />.</param>
public record DhcpRaInterfaceOverrideDetail(
    string InterfaceName,
    bool Exclude,
    bool? ManagedAddressConfiguration,
    bool? OtherConfiguration,
    TimeSpan? RouterLifetime,
    int? CurrentHopLimit,
    TimeSpan? ReachableTime,
    TimeSpan? RetransTimer,
    uint? LinkMtu,
    IReadOnlyList<string> RecursiveDnsServers,
    TimeSpan? RecursiveDnsServerLifetime,
    IReadOnlyList<string> DnsSearchListDomains,
    TimeSpan? DnsSearchListLifetime
);

/// <summary>
///     This enumeration names each of the four DHCP-family services
///     <see cref="Controllers.DhcpServiceControlController" /> reports on/controls -- one entry
///     per real <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> implementation this project
///     registers. The <c>{kind}</c> route segment on
///     <see cref="Controllers.DhcpServiceControlController.Start" />/
///     <see cref="Controllers.DhcpServiceControlController.Stop" /> binds directly to this
///     enumeration (case-insensitively, ASP.NET Core's own default model-binding behavior for an
///     enum route parameter), so an unrecognized route segment is rejected by model binding itself
///     before this controller's own action ever runs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DhcpServiceKindEnum>))]
public enum DhcpServiceKindEnum
{
    /// <summary>The DHCPv4 UDP transport (<c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>).</summary>
    Dhcpv4,

    /// <summary>The DHCPv6 UDP transport (<c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener</c>).</summary>
    Dhcpv6,

    /// <summary>
    ///     The Router Advertisement raw ICMPv6 transport
    ///     (<c>Ninlil.Dhcp.Protocol.Ra.Icmpv6RaListener</c>).
    /// </summary>
    RouterAdvertisement,

    /// <summary>The read-only RFC 1350 TFTP server (<c>Ninlil.Dhcp.Server.Tftp.TftpServer</c>).</summary>
    Tftp,
}

/// <summary>
///     This record is the REST representation of one DHCP-family service's own current state --
///     both its configured <see cref="Enabled" /> flag (whether it starts automatically at host
///     boot) and its actual, live <see cref="IsRunning" /> signal, which can legitimately differ
///     from it in either direction: a service configured <see langword="false" /> can still be
///     running right now (started live, after boot, through this same controller), and a service
///     configured <see langword="true" /> can legitimately not be running (stopped live, or --
///     <see cref="DhcpServiceKindEnum.Tftp" /> only -- declined to bind at all for a real,
///     non-throwing precondition failure such as a missing root directory; see
///     <c>TftpServer</c>'s own doc comment) -- see
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c>'s own doc comment for why these
///     two are deliberately distinct concepts throughout this whole feature.
/// </summary>
/// <param name="Kind">Which of the four DHCP-family services this status describes.</param>
/// <param name="Enabled">
///     Whether this service is configured to start automatically at the next host boot -- this
///     server's own <c>Enable</c> option for this protocol, read fresh on every call, never a
///     stale snapshot.
/// </param>
/// <param name="IsRunning">Whether this service's own listener is genuinely bound and serving traffic right now.</param>
public record DhcpServiceStatus(DhcpServiceKindEnum Kind, bool Enabled, bool IsRunning);
