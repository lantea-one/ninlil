using System.Text.Json.Serialization;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a DHCP scope
///     (<c>POST /api/dhcp-scopes</c>, <c>PUT /api/dhcp-scopes/{id}</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the scope.</param>
/// <param name="ProtocolVersion">Whether this is a DHCPv4 or a DHCPv6 scope.</param>
/// <param name="Kind">
///     What the scope hands out: addresses, or (DHCPv6 only) RFC 8415 delegated prefixes.
///     Defaults to <see cref="DhcpScopeKindEnum.AddressAssignment" />, the only valid value for a
///     DHCPv4 scope (see <c>DhcpScopeEntity.Kind</c>).
/// </param>
/// <param name="RangeStartAddress">The first address of the pool the scope leases from, inclusive.</param>
/// <param name="RangeEndAddress">The last address of the pool the scope leases from, inclusive.</param>
/// <param name="SubnetPrefixLength">The prefix length, in bits, of the network the range belongs to.</param>
/// <param name="DefaultLeaseDuration">The lease duration granted to a client that does not ask for one.</param>
/// <param name="MaxLeaseDuration">The longest lease duration the scope grants.</param>
/// <param name="IsDisabled">Whether the scope is disabled.</param>
/// <param name="GatewayAddresses">The router (default gateway) addresses the scope hands out, in order of preference.</param>
/// <param name="DnsServerAddresses">The DNS server addresses the scope hands out, in order of preference.</param>
/// <param name="Comments">Free-form operator notes about the scope.</param>
/// <param name="DnsZoneId">
///     The DNS zone in which a lease from this scope registers an A or AAAA record for the
///     client's host name, or <see langword="null" /> to turn off DHCP-to-DNS registration for
///     the scope.
/// </param>
/// <param name="BootFileName">
///     The boot file a PXE client leasing from this scope should request, or
///     <see langword="null" /> to offer none (see <c>DhcpScopeEntity.BootFileName</c>).
/// </param>
/// <param name="NextServerAddress">
///     The TFTP (or other boot) server a PXE client should contact for
///     <paramref name="BootFileName" />, or <see langword="null" /> to use this server's Server
///     Identifier address (see <c>DhcpScopeEntity.NextServerAddress</c>).
/// </param>
/// <param name="DelegatedPrefixLength">
///     The length, in bits, of each prefix the scope delegates. Required when
///     <paramref name="Kind" /> is <see cref="DhcpScopeKindEnum.PrefixDelegation" />, and must be
///     <see langword="null" /> otherwise (see <c>DhcpScopeEntity.DelegatedPrefixLength</c>).
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
///     This class carries a DHCP scope, as returned by the list, get and write endpoints of
///     <c>/api/dhcp-scopes</c>. The fields mean what they mean in
///     <see cref="DhcpScopeWriteRequest" />.
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
///     This record is the REST shape of <c>Ninlil.Dhcp.Server.Core.Statistics.DhcpScopeStatistics</c>,
///     one scope's utilization (<c>GET /api/dhcp-scopes/{id}/statistics</c>). The figures are
///     computed on each request, not cached; that class documents how. <see cref="TotalSlots" />
///     and <see cref="AvailableSlots" /> are decimal strings rather than JSON numbers because an
///     IPv6 range can hold far more addresses than a JSON number represents exactly (a
///     <c>/64</c> holds 2^64).
/// </summary>
/// <param name="ScopeId">The identifier of the scope the figures describe.</param>
/// <param name="ScopeName">The name of the scope the figures describe.</param>
/// <param name="ProtocolVersion">Whether this is a DHCPv4 or a DHCPv6 scope.</param>
/// <param name="Kind">What the scope hands out (see <c>DhcpScopeEntity.Kind</c>).</param>
/// <param name="TotalSlots">
///     How many slots the scope's range holds, as a decimal string. A slot is an address for an
///     <see cref="DhcpScopeKindEnum.AddressAssignment" /> scope, or a block-aligned delegable
///     prefix for a <see cref="DhcpScopeKindEnum.PrefixDelegation" /> scope.
/// </param>
/// <param name="LeasedCount">How many slots are actively leased.</param>
/// <param name="OfferedCount">How many slots are offered and awaiting the client's request.</param>
/// <param name="ReservedCount">How many enabled static reservations the scope has.</param>
/// <param name="AvailableSlots">
///     <paramref name="TotalSlots" /> minus every distinct slot held by a blocking lease or an
///     enabled reservation, as a decimal string; never negative.
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
///     in a DHCP scope (<c>POST</c> and <c>PUT</c> under <c>/api/dhcp-scopes/{scopeId}/reservations</c>).
/// </summary>
/// <param name="ClientIdentifier">The client's identifier: a MAC address for DHCPv4, or a DUID for DHCPv6, in hex. Accepted spellings are <c>aa:bb:cc:dd:ee:ff</c>, <c>aa-bb-cc-dd-ee-ff</c>, <c>aabb.ccdd.eeff</c> and <c>aabbccddeeff</c> (up to 85 bytes); it is stored and returned as <c>aa:bb:cc:dd:ee:ff</c>.</param>
/// <param name="IpAddress">The fixed IP address reserved for the client; it is stored and returned in canonical form (for example <c>2001:db8::10</c>).</param>
/// <param name="HostName">An operator-supplied host name for the reservation, or <see langword="null" /> for none.</param>
/// <param name="IsDisabled">Whether the reservation is disabled.</param>
/// <param name="Comments">Free-form operator notes about the reservation.</param>
public record DhcpReservationWriteRequest(
    string ClientIdentifier,
    string IpAddress,
    string HostName = null,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a DHCP reservation, as returned by the list, get and write endpoints
///     under <c>/api/dhcp-scopes/{scopeId}/reservations</c>.
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
///     in a scope (<c>POST</c> and <c>PUT</c> under <c>/api/dhcp-scopes/{scopeId}/custom-options</c>).
/// </summary>
/// <param name="OptionTag">
///     The option's numeric tag. Use 1 to 254, except 52 (Option Overload), 53 (DHCP Message Type)
///     and 82 (Relay Agent Information), which the server or relay agent sets itself. 0 (Pad) and
///     255 (End) are reserved markers. The value is at most 255 bytes.
/// </param>
/// <param name="ValueHex">
///     The option's raw value bytes as a hexadecimal string with an even number of digits; it is
///     stored in uppercase. It is not used, and may be blank, when
///     <paramref name="ValueSource" /> is <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
/// </param>
/// <param name="IsDisabled">Whether the custom option is disabled.</param>
/// <param name="ValueSource">
///     Where the delivered value comes from (see <see cref="DhcpOptionValueSourceEnum" />).
///     Defaults to <see cref="DhcpOptionValueSourceEnum.Custom" />, which sends
///     <paramref name="ValueHex" />.
/// </param>
/// <param name="Comments">Free-form operator notes about the custom option.</param>
public record DhcpScopeCustomOptionWriteRequest(
    byte OptionTag,
    string ValueHex,
    bool IsDisabled = false,
    DhcpOptionValueSourceEnum ValueSource = DhcpOptionValueSourceEnum.Custom,
    string Comments = null
);

/// <summary>
///     This class carries a scope's custom option, as returned by the list, get and write
///     endpoints under <c>/api/dhcp-scopes/{scopeId}/custom-options</c>.
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
///     option delivery rule (<c>POST /api/dhcp-tag-rules</c>, <c>PUT /api/dhcp-tag-rules/{id}</c>).
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity</c> documents how rules match,
///     the order they apply in and how conflicts resolve. Every match field is optional; the set
///     ones must all match.
/// </summary>
/// <param name="Name">An operator-chosen, descriptive name for the rule.</param>
/// <param name="ScopeId">
///     The one scope the rule applies to, or <see langword="null" /> for clients of any scope.
/// </param>
/// <param name="Priority">The rule's evaluation order, ascending; a later rule's option overrides an earlier one's. Defaults to 0.</param>
/// <param name="IsDisabled">Whether the rule is disabled.</param>
/// <param name="VendorClassIdentifierMatch">The Vendor Class Identifier value the rule matches, or <see langword="null" />.</param>
/// <param name="VendorClassIdentifierMatchMode">How <paramref name="VendorClassIdentifierMatch" /> is compared.</param>
/// <param name="UserClassMatch">The User Class value the rule matches, or <see langword="null" />.</param>
/// <param name="UserClassMatchMode">How <paramref name="UserClassMatch" /> is compared.</param>
/// <param name="RelayAgentCircuitIdHex">The Agent Circuit ID the rule requires, as hex, or <see langword="null" /> (DHCPv4 only).</param>
/// <param name="RelayAgentRemoteIdHex">The relay agent (DHCPv4) or DHCPv6 Remote ID the rule requires, as hex, or <see langword="null" />.</param>
/// <param name="RelayInterfaceIdHex">The DHCPv6 Interface-ID the rule requires, as hex, or <see langword="null" /> (DHCPv6 only).</param>
/// <param name="VendorClassEnterpriseNumber">The IANA Enterprise Number a DHCPv6 Vendor Class option must carry, or <see langword="null" /> (DHCPv6 only).</param>
/// <param name="Comments">Free-form operator notes about the rule.</param>
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
///     This class carries a tag rule, as returned by the list, get and write endpoints of
///     <c>/api/dhcp-tag-rules</c>.
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
///     contributes (<c>POST</c> and <c>PUT</c> under <c>/api/dhcp-tag-rules/{tagRuleId}/options</c>).
/// </summary>
/// <param name="OptionTag">
///     The option's numeric tag, as in <see cref="DhcpScopeCustomOptionWriteRequest.OptionTag" />.
///     Those limits apply to a global rule and to a rule for a DHCPv4 scope. A rule for a DHCPv6
///     scope only rejects tag 0 and values over 65 535 bytes.
/// </param>
/// <param name="ValueHex">
///     The option's raw value bytes as hexadecimal, as in
///     <see cref="DhcpScopeCustomOptionWriteRequest.ValueHex" />. It is not used, and may be
///     blank, when <paramref name="ValueSource" /> is
///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
/// </param>
/// <param name="ValueSource">
///     Where the delivered value comes from, as in
///     <see cref="DhcpScopeCustomOptionWriteRequest.ValueSource" />.
/// </param>
public record DhcpTagRuleOptionWriteRequest(
    byte OptionTag,
    string ValueHex,
    DhcpOptionValueSourceEnum ValueSource = DhcpOptionValueSourceEnum.Custom
);

/// <summary>
///     This class carries one option of a tag rule, as returned by the list, get and write
///     endpoints under <c>/api/dhcp-tag-rules/{tagRuleId}/options</c>.
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
///     This class carries one DHCP lease, as returned by the list and get endpoints of
///     <c>/api/dhcp-leases</c>. There is no write request: the allocation engine creates and
///     updates leases as clients come and go, and an operator can only release one
///     (<c>POST /api/dhcp-leases/{id}/release</c>). <see cref="PrefixLength" /> is set only for a
///     DHCPv6 Prefix Delegation lease and is <see langword="null" /> for an address lease (see
///     <c>LeaseEntity.PrefixLength</c>).
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
///     This record is the REST shape of part of <c>Ninlil.Dhcp.Server.Core.DhcpServerOptions</c>,
///     the settings shared by the local DHCP engines (<c>GET /api/dhcp-settings/core</c>).
/// </summary>
/// <param name="ServerIdentifierAddress">
///     The IPv4 address the server identifies itself as, or <see langword="null" /> to detect it
///     at startup.
/// </param>
/// <param name="OfferHoldDuration">How long an offered address is held for the client before it is reclaimed. Defaults to 30 seconds.</param>
/// <param name="DeclinedAddressQuarantineDuration">How long an address a client declined is kept out of use. Defaults to 10 minutes.</param>
public record DhcpCoreSettingsDetail(
    string ServerIdentifierAddress,
    TimeSpan OfferHoldDuration,
    TimeSpan DeclinedAddressQuarantineDuration
);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions</c>,
///     the DHCPv4 UDP listener's settings (<c>GET /api/dhcp-settings/protocol</c>).
/// </summary>
/// <param name="Enable">Whether the listener starts at boot and answers DHCPv4 traffic. Defaults to <see langword="false" />.</param>
/// <param name="ServerPort">The port the listener binds to. Defaults to 67.</param>
/// <param name="ClientPort">The port a reply sent directly to a client goes to. Defaults to 68.</param>
public record DhcpProtocolSettingsDetail(bool Enable, int ServerPort, int ClientPort);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Server.Relay.Dhcpv4RelayOptions</c>
///     (<c>GET /api/dhcp-settings/relay</c>): whether the server relays DHCPv4 requests to
///     upstream servers instead of answering them from its own scopes.
/// </summary>
/// <param name="Enable">Whether the server acts as a DHCPv4 relay agent. Defaults to <see langword="false" />.</param>
/// <param name="UpstreamServerAddresses">The IPv4 addresses of the upstream DHCPv4 servers requests are forwarded to.</param>
/// <param name="UpstreamServerPort">The UDP port the upstream servers listen on. Defaults to 67.</param>
/// <param name="RelayAgentAddress">
///     The IPv4 address the relay puts in <c>giaddr</c>, or <see langword="null" /> to detect it
///     at startup.
/// </param>
/// <param name="RelayAgentCircuitId">The Agent Circuit ID text added to every forwarded request, or <see langword="null" /> for none. Printable ASCII, at most 253 characters, and at most 251 together with the Remote ID.</param>
/// <param name="RelayAgentRemoteId">The Agent Remote ID text added to every forwarded request, or <see langword="null" /> for none. Printable ASCII, at most 253 characters, and at most 251 together with the Circuit ID.</param>
public record DhcpRelaySettingsDetail(
    bool Enable,
    IReadOnlyList<string> UpstreamServerAddresses,
    int UpstreamServerPort,
    string RelayAgentAddress,
    string RelayAgentCircuitId,
    string RelayAgentRemoteId
);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ProtocolOptions</c>,
///     the DHCPv6 UDP listener's settings (<c>GET /api/dhcp-settings/dhcpv6-protocol</c>); the
///     DHCPv6 counterpart of <see cref="DhcpProtocolSettingsDetail" />.
/// </summary>
/// <param name="Enable">Whether the listener starts at boot and answers DHCPv6 traffic. Defaults to <see langword="false" />.</param>
/// <param name="ServerPort">The port the listener binds to. Defaults to 547, the IANA DHCPv6 server port.</param>
/// <param name="ClientPort">The port a reply is unicast to. Defaults to 546, the IANA DHCPv6 client port.</param>
public record Dhcpv6ProtocolSettingsDetail(bool Enable, int ServerPort, int ClientPort);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayOptions</c>
///     (<c>GET /api/dhcp-settings/dhcpv6-relay</c>): whether the server acts as an RFC 8415
///     DHCPv6 relay agent instead of answering from its own scopes. It is the DHCPv6 counterpart
///     of <see cref="DhcpRelaySettingsDetail" />.
/// </summary>
/// <param name="Enable">Whether the server acts as a DHCPv6 relay agent. Defaults to <see langword="false" />.</param>
/// <param name="UpstreamServerAddresses">The IPv6 addresses of the upstream DHCPv6 servers RELAY-FORW messages are sent to.</param>
/// <param name="UpstreamServerPort">The UDP port the upstream servers listen on. Defaults to 547.</param>
/// <param name="RelayLinkAddress">
///     The IPv6 address the relay puts in the RELAY-FORW "link-address" field, or
///     <see langword="null" /> to detect it at startup.
/// </param>
/// <param name="InterfaceId">The RFC 8415 Section 21.18 Interface-ID text added to every RELAY-FORW, or <see langword="null" /> for none.</param>
public record Dhcpv6RelaySettingsDetail(
    bool Enable,
    IReadOnlyList<string> UpstreamServerAddresses,
    int UpstreamServerPort,
    string RelayLinkAddress,
    string InterfaceId
);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Server.Tftp.TftpServerOptions</c>, the
///     bundled read-only RFC 1350 TFTP server (<c>GET /api/dhcp-settings/tftp</c>).
/// </summary>
/// <param name="Enable">Whether the TFTP server starts at boot. Defaults to <see langword="false" />.</param>
/// <param name="Port">The UDP port the TFTP server listens on. Defaults to 69.</param>
/// <param name="RootDirectory">The directory files are served from. Empty by default.</param>
/// <param name="RetransmitTimeoutSeconds">How long, in seconds, to wait for an acknowledgment before sending a block again. Defaults to 2.</param>
/// <param name="MaxRetransmissions">How many times one block is sent again before the transfer is abandoned. Defaults to 5.</param>
/// <param name="MaxConcurrentTransfers">How many transfers may run at once, at most 4 per client address. A request over the limit is dropped without a reply. At least 1. Defaults to 50.</param>
public record DhcpTftpSettingsDetail(
    bool Enable,
    int Port,
    string RootDirectory,
    double RetransmitTimeoutSeconds,
    int MaxRetransmissions,
    int MaxConcurrentTransfers
);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Protocol.Ra.RaProtocolOptions</c>
///     (<c>GET /api/dhcp-settings/ra-protocol</c>): whether the Router Advertisement listener runs
///     and how often it sends unsolicited advertisements. What an advertisement contains is in
///     <see cref="DhcpRaSettingsDetail" />, following the same split in the options classes.
/// </summary>
/// <param name="Enable">Whether the listener starts at boot, sending and answering Router Advertisement traffic. Defaults to <see langword="false" />.</param>
/// <param name="MinRouterAdvertisementInterval">The shortest interval between two unsolicited advertisements on one interface. Defaults to 198 seconds.</param>
/// <param name="MaxRouterAdvertisementInterval">The longest interval between two unsolicited advertisements on one interface. Defaults to 600 seconds.</param>
public record DhcpRaProtocolSettingsDetail(
    bool Enable,
    TimeSpan MinRouterAdvertisementInterval,
    TimeSpan MaxRouterAdvertisementInterval
);

/// <summary>
///     This record is the REST shape of <c>Ninlil.Dhcp.Server.Ra.RaOptions</c>
///     (<c>GET /api/dhcp-settings/ra</c>): what the server's Router Advertisements contain. The
///     transport and timing settings are in <see cref="DhcpRaProtocolSettingsDetail" />.
///     <para>
///         <b><c>RaOptions.Prefixes</c> is not included.</b> Each advertised prefix has its own
///         on-link and autonomous flags and two lifetimes, which suits a separate sub-resource
///         (as <c>DhcpReservationsController</c> gives reservations) better than a field here.
///         Prefixes are configured in the configuration file only.
///     </para>
///     <para>
///         <b><see cref="Routes" /> is included.</b> An RFC 4191 route is a simple triple (prefix
///         and length, preference, lifetime), so a <c>PATCH</c> replaces the whole list, as it
///         does <see cref="RecursiveDnsServers" /> and <see cref="DnsSearchListDomains" />.
///         <c>RaOptions</c> lives in the JSON configuration file, not the database, so its list
///         entries have no stable identifiers to build a sub-resource on. Every change needs a
///         restart.
///     </para>
/// </summary>
/// <param name="ManagedAddressConfiguration">The RFC 4861 "M" flag: hosts should get addresses from stateful DHCPv6. Defaults to <see langword="false" />.</param>
/// <param name="OtherConfiguration">The RFC 4861 "O" flag: hosts should use DHCPv6 for configuration other than addresses. Defaults to <see langword="false" />.</param>
/// <param name="RouterLifetime">How long a host may use this server as a default router; zero (the default) means not at all.</param>
/// <param name="CurrentHopLimit">The hop limit recommended for outgoing unicast packets; zero means no recommendation. Defaults to 64.</param>
/// <param name="ReachableTime">How long a neighbor is assumed reachable after confirmation; zero (the default) means no recommendation.</param>
/// <param name="RetransTimer">How long a host waits before sending a Neighbor Solicitation again; zero (the default) means no recommendation.</param>
/// <param name="LinkMtu">The link MTU to advertise, or <see langword="null" /> (the default) to send no MTU option.</param>
/// <param name="RecursiveDnsServers">The Recursive DNS Server addresses (RFC 8106 Section 5.1) advertised to SLAAC hosts.</param>
/// <param name="RecursiveDnsServerLifetime">How long a host may use <paramref name="RecursiveDnsServers" />. Defaults to 1800 seconds.</param>
/// <param name="DnsSearchListDomains">The DNS Search List domains (RFC 8106 Section 5.2) advertised to SLAAC hosts.</param>
/// <param name="DnsSearchListLifetime">How long a host may use <paramref name="DnsSearchListDomains" />. Defaults to 1800 seconds.</param>
/// <param name="Routes">The specific routes advertised in RFC 4191 Section 2.3 Route Information options.</param>
/// <param name="InterfaceOverrides">
///     Per-interface content overrides. <see cref="DhcpRaInterfaceOverrideDetail" /> explains how
///     a <see langword="null" /> field inherits the value above, and why its prefix and route
///     lists are not exposed here.
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
///     This record is the REST shape of one <c>Ninlil.Dhcp.Server.Ra.RaAdvertisedRouteOptions</c>
///     entry: a route advertised in an RFC 4191 Section 2.3 Route Information option. It appears
///     in <see cref="DhcpRaSettingsDetail.Routes" /> and in the <c>routes</c> array of
///     <c>PATCH /api/dhcp-settings/ra</c>.
/// </summary>
/// <param name="Prefix">The route's prefix, or <see langword="null" /> or empty when <paramref name="PrefixLength" /> is <c>0</c> (an entry that only sets the default route's preference).</param>
/// <param name="PrefixLength">The number of significant leading bits of <paramref name="Prefix" />, 0 to 128. Defaults to 64.</param>
/// <param name="Preference">The route's preference (RFC 4191 Section 2.1) relative to other routes and default routers on the link. Defaults to Medium.</param>
/// <param name="RouteLifetime">How long the route stays valid, counted from each advertisement that carries it. Defaults to 1800 seconds.</param>
public record DhcpRaRouteDetail(
    string Prefix,
    int PrefixLength,
    RaRoutePreferenceEnum Preference,
    TimeSpan RouteLifetime
);

/// <summary>
///     This record is the REST shape of one <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c>
///     entry: different Router Advertisement content for one network interface. It appears in
///     <see cref="DhcpRaSettingsDetail.InterfaceOverrides" /> and in the
///     <c>interfaceOverrides</c> array of <c>PATCH /api/dhcp-settings/ra</c>. A
///     <see langword="null" /> scalar field inherits the matching global field. A
///     <see langword="null" /> <see cref="RecursiveDnsServers" /> or
///     <see cref="DnsSearchListDomains" /> inherits the global list; any other value, even an
///     empty list, replaces it. <c>RaInterfaceOverrideOptions</c> has the full rules.
///     <para>
///         <b>The override's <c>Prefixes</c> and <c>Routes</c> lists are not included.</b>
///         Prefixes are file-only globally too (see <see cref="DhcpRaSettingsDetail" />), and a
///         route list nested inside this list would make the API and UI much more complex. Both
///         can be set in the JSON configuration file
///         (<c>RaInterfaceOverrideOptions.Prefixes</c> and <c>.Routes</c>).
///     </para>
/// </summary>
/// <param name="InterfaceName">The OS-reported network interface name this override applies to (for example <c>eth0</c>), matched case-insensitively.</param>
/// <param name="Exclude">When <see langword="true" />, no Router Advertisements are sent on this interface and every other field is ignored.</param>
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
///     This enumeration names the four DHCP-family services that
///     <see cref="Controllers.DhcpServiceControlController" /> reports on and controls, one per
///     registered <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> implementation. The
///     <c>{kind}</c> route segment of <see cref="Controllers.DhcpServiceControlController.Start" />
///     and <see cref="Controllers.DhcpServiceControlController.Stop" /> binds to it
///     case-insensitively (the ASP.NET Core default for an enum), so an unknown segment fails model
///     binding before the action runs.
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
///     This record is one DHCP-family service's state, as returned by
///     <c>/api/dhcp-services</c>: its configured <see cref="Enabled" /> flag (whether it starts at
///     boot) and whether it <see cref="IsRunning" />. The two can differ either way. A service
///     configured off can be running because it was started through this controller, and one
///     configured on can be stopped, either through this controller or, for
///     <see cref="DhcpServiceKindEnum.Tftp" /> only, because it declined to bind (for example when
///     its root directory is missing; see <c>TftpServer</c>).
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> explains the distinction.
/// </summary>
/// <param name="Kind">Which of the four DHCP-family services this status describes.</param>
/// <param name="Enabled">
///     Whether the service's <c>Enable</c> option says it starts at boot. It is read from the
///     settings loaded at startup, so a saved change shows only after a restart.
/// </param>
/// <param name="IsRunning">Whether the service's listener is bound and serving traffic now.</param>
public record DhcpServiceStatus(DhcpServiceKindEnum Kind, bool Enabled, bool IsRunning);
