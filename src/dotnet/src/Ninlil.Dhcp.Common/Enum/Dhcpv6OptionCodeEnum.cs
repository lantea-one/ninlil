// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names the DHCPv6 option codes (RFC 8415, plus RFC 3646, RFC 4649 and RFC
///     4704 options) that the code works with, most of them through typed accessors in
///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6MessageOptionExtensions" />. It is not a full list
///     of IANA codes: a message can carry any other code as a raw
///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6Option" />. It is the DHCPv6 counterpart of
///     <see cref="Dhcpv4OptionTagEnum" />, without Pad or End, since every DHCPv6 option is a
///     plain code/length/value entry.
/// </summary>
public enum Dhcpv6OptionCodeEnum : ushort
{
    /// <summary>
    ///     RFC 8415 Section 21.2's Client Identifier option, carrying the client's DUID (DHCP
    ///     Unique Identifier). A DUID is treated as an opaque byte string, like
    ///     <see cref="Dhcpv4OptionTagEnum.ClientIdentifier" />; RFC 8415 Section 11 defines several
    ///     DUID formats, but the wire layer does not need to tell them apart.
    /// </summary>
    ClientIdentifier = 1,

    /// <summary>
    ///     RFC 8415 Section 21.3's Server Identifier option: a DUID like
    ///     <see cref="ClientIdentifier" />, identifying the server.
    /// </summary>
    ServerIdentifier = 2,

    /// <summary>
    ///     RFC 8415 Section 21.10's Relay Message option. It appears only in a RELAY-FORW or
    ///     RELAY-REPL message (see <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessage" />), and
    ///     its value is the encoded message being relayed: the client's request or the server's
    ///     reply. The relay codec supports one level of nesting; see
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessageReader" /> for why a nested
    ///     RELAY-FORW or RELAY-REPL is rejected.
    /// </summary>
    RelayMessage = 9,

    /// <summary>
    ///     RFC 8415 Section 21.4's Identity Association for Non-temporary Addresses (IA_NA)
    ///     option: a container carrying one client-chosen IAID, the T1/T2 renewal timers, and a
    ///     nested options area (typically one or more <see cref="IaAddress" /> options, plus
    ///     optionally a <see cref="StatusCode" /> option). See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForNonTemporaryAddresses" />.
    /// </summary>
    IaNa = 3,

    /// <summary>
    ///     RFC 8415 Section 21.5's Identity Association for Temporary Addresses (IA_TA) option.
    ///     It nests <see cref="IaAddress" /> options like <see cref="IaNa" />, but has no T1/T2
    ///     timers. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForTemporaryAddresses" />.
    /// </summary>
    IaTa = 4,

    /// <summary>
    ///     RFC 8415 Section 21.6's IA Address (IAADDR) option: one address with its preferred and
    ///     valid lifetimes, nested inside an <see cref="IaNa" /> or <see cref="IaTa" /> option.
    ///     See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationAddress" />.
    /// </summary>
    IaAddress = 5,

    /// <summary>
    ///     RFC 8415 Section 21.7's Option Request Option (ORO): the option codes a client wants in
    ///     the reply, the DHCPv6 counterpart of the Parameter Request List
    ///     (<see cref="Dhcpv4OptionTagEnum.ParameterRequestList" />).
    /// </summary>
    OptionRequest = 6,

    /// <summary>
    ///     RFC 8415 Section 21.8's Preference option. A server includes it in an ADVERTISE to
    ///     influence which server a client picks.
    /// </summary>
    Preference = 7,

    /// <summary>
    ///     RFC 8415 Section 21.9's Elapsed Time option: how long, in hundredths of a second,
    ///     the client has been trying to complete the current message exchange.
    /// </summary>
    ElapsedTime = 8,

    /// <summary>
    ///     RFC 8415 Section 21.15's User Class option: one or more "user-class-data" instances,
    ///     each a 2-octet length followed by opaque data. It is the counterpart of the DHCPv4 User
    ///     Class option (<see cref="Dhcpv4OptionTagEnum.UserClass" />, RFC 3004), which uses a
    ///     1-octet length.
    /// </summary>
    UserClass = 15,

    /// <summary>
    ///     RFC 8415 Section 21.16's Vendor Class option: a 4-octet IANA Enterprise Number followed
    ///     by one or more "vendor-class-data" instances, each a 2-octet length and opaque data.
    ///     Unlike the single-string DHCPv4 Vendor Class Identifier
    ///     (<see cref="Dhcpv4OptionTagEnum.VendorClassIdentifier" />, RFC 2132 Section 9.13), it
    ///     names the vendor's Enterprise Number explicitly.
    /// </summary>
    VendorClass = 16,

    /// <summary>
    ///     RFC 8415 Section 21.13's Status Code option: a numeric result code
    ///     (<see cref="Dhcpv6StatusCodeEnum" />) plus an optional human-readable message, nested
    ///     inside an IA_NA/IA_TA/IA_PD option to report success/failure for that specific identity
    ///     association, or carried at the top level of a message to report success/failure for
    ///     the whole exchange.
    /// </summary>
    StatusCode = 13,

    /// <summary>
    ///     RFC 8415 Section 21.14's Rapid Commit option: an empty marker a client puts in a
    ///     SOLICIT to ask for an immediate REPLY (skipping ADVERTISE and REQUEST), and a server
    ///     puts in that REPLY to confirm it.
    /// </summary>
    RapidCommit = 14,

    /// <summary>
    ///     RFC 3646 Section 3's DNS Recursive Name Server option: one or more recursive DNS server
    ///     addresses in order of preference, the counterpart of the DHCPv4 Domain Name Server
    ///     option (<see cref="Dhcpv4OptionTagEnum.DomainNameServer" />).
    /// </summary>
    DnsRecursiveNameServer = 23,

    /// <summary>
    ///     RFC 3646's Domain Search List option: one or more uncompressed DNS wire-format names,
    ///     back to back, for a client to search when resolving an unqualified host name.
    /// </summary>
    DomainSearchList = 24,

    /// <summary>
    ///     RFC 8415 Section 21.21's Identity Association for Prefix Delegation (IA_PD) option,
    ///     the prefix delegation counterpart of <see cref="IaNa" />: a container carrying one
    ///     client-chosen IAID, T1/T2 renewal timers, and a nested options area of one or more
    ///     <see cref="IaPrefix" /> options. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForPrefixDelegation" />.
    /// </summary>
    IaPd = 25,

    /// <summary>
    ///     RFC 8415 Section 21.22's IA Prefix (IAPREFIX) option: one delegated prefix with its
    ///     preferred and valid lifetimes, nested inside an <see cref="IaPd" /> option (the prefix
    ///     counterpart of <see cref="IaAddress" />). See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationPrefix" />.
    /// </summary>
    IaPrefix = 26,

    /// <summary>
    ///     RFC 8415 Section 21.18's Interface-ID option: opaque bytes a relay agent puts on a
    ///     RELAY-FORW to identify the interface a client's message arrived on. The server must
    ///     echo it unchanged on the RELAY-REPL. The value is treated as an opaque byte string and
    ///     never interpreted.
    /// </summary>
    InterfaceId = 18,

    /// <summary>
    ///     RFC 4649's Relay Agent Remote-ID option (OPTION_REMOTE_ID): a 4-octet IANA Enterprise
    ///     Number followed by an opaque "remote-id" a relay agent uses to identify the client or
    ///     its attachment point. It sits in the RELAY-FORW options next to (not inside) the Relay
    ///     Message option, and is the counterpart of the RFC 3046 DHCPv4 Agent Remote ID
    ///     sub-option. The whole value is treated as one opaque byte string.
    /// </summary>
    RemoteId = 37,

    /// <summary>
    ///     RFC 4704's Client FQDN option: the client's fully qualified (or partial) domain name,
    ///     plus the N/O/S flags that say whether client or server performs the AAAA/PTR DNS
    ///     updates. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6MessageOptionExtensions.TryGetClientFqdn" /> for
    ///     the flag meanings (RFC 4704 Section 4.1).
    /// </summary>
    ClientFqdn = 39,
}
