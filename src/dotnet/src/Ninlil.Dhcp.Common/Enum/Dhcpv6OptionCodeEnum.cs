// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names the RFC 8415 (plus RFC 3646's DNS-configuration options, folded
///     into the same base wire format) option codes this project currently understands well
///     enough to give a strongly-typed accessor for (see
///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6MessageOptionExtensions" />). It is deliberately
///     not exhaustive of every option code IANA has ever assigned -- a message can carry any
///     other code's raw <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6Option" /> perfectly well without
///     this project needing to name it here first; a future option (needed once
///     <c>Ninlil.Dhcp.Server.Core</c>'s own DHCPv6 engine exists) simply adds its own code here
///     plus its own accessor pair, with no change needed to the reader, writer, or this
///     enumeration's existing members. This is the DHCPv6 analogue of
///     <see cref="Dhcpv4OptionTagEnum" />; unlike that one, DHCPv6 has no Pad/End structural
///     framing codes to carry here -- every DHCPv6 option is an ordinary tag/length/value entry.
/// </summary>
public enum Dhcpv6OptionCodeEnum : ushort
{
    /// <summary>
    ///     RFC 8415 Section 21.2's Client Identifier option -- carries the client's own DUID
    ///     (DHCP Unique Identifier). This project treats a DUID as an opaque byte string, the same
    ///     way <see cref="Dhcpv4OptionTagEnum.ClientIdentifier" /> is treated -- RFC 8415 Section
    ///     11 defines several DUID sub-formats, but nothing in this project's own wire/transport
    ///     layer needs to distinguish them.
    /// </summary>
    ClientIdentifier = 1,

    /// <summary>
    ///     RFC 8415 Section 21.3's Server Identifier option -- the same DUID shape as
    ///     <see cref="ClientIdentifier" />, carrying the server's own identity instead.
    /// </summary>
    ServerIdentifier = 2,

    /// <summary>
    ///     RFC 8415 Section 21.10's Relay Message option -- carried only inside a RELAY-FORW/
    ///     RELAY-REPL message's own options area (see
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessage" />), never at an ordinary
    ///     client/server message's own top level. Its value is the complete, raw, still-encoded
    ///     bytes of the message being relayed -- a client's original request, or a server's own
    ///     reply -- nested one level deep. This project's own relay codec supports exactly one
    ///     level of nesting (a single relay hop); see
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessageReader" />'s own doc comment for why
    ///     a nested payload that is itself another RELAY-FORW/RELAY-REPL is rejected rather than
    ///     silently misread.
    /// </summary>
    RelayMessage = 9,

    /// <summary>
    ///     RFC 8415 Section 21.4's Identity Association for Non-temporary Addresses (IA_NA)
    ///     option -- a container carrying one client-chosen IAID, the T1/T2 renewal timers, and a
    ///     nested options area (typically one or more <see cref="IaAddress" /> options, plus
    ///     optionally a <see cref="StatusCode" /> option). See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForNonTemporaryAddresses" />.
    /// </summary>
    IaNa = 3,

    /// <summary>
    ///     RFC 8415 Section 21.5's Identity Association for Temporary Addresses (IA_TA) option --
    ///     the same nested-<see cref="IaAddress" /> shape as <see cref="IaNa" />, but with no
    ///     T1/T2 renewal timers of its own (a temporary address is never renewed, only
    ///     re-requested). See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForTemporaryAddresses" />.
    /// </summary>
    IaTa = 4,

    /// <summary>
    ///     RFC 8415 Section 21.6's IA Address (IAADDR) option -- one specific address plus its own
    ///     preferred/valid lifetimes, nested inside an <see cref="IaNa" />/<see cref="IaTa" />
    ///     option's own options area. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationAddress" />.
    /// </summary>
    IaAddress = 5,

    /// <summary>
    ///     RFC 8415 Section 21.7's Option Request Option (ORO) -- the list of other option codes a
    ///     client is asking a server to include in its reply, the DHCPv6 analogue of DHCPv4's
    ///     Parameter Request List (<see cref="Dhcpv4OptionTagEnum.ParameterRequestList" />).
    /// </summary>
    OptionRequest = 6,

    /// <summary>
    ///     RFC 8415 Section 21.8's Preference option -- a server includes this in an ADVERTISE to
    ///     influence how eagerly a client should prefer it over a competing server's own
    ///     ADVERTISE.
    /// </summary>
    Preference = 7,

    /// <summary>
    ///     RFC 8415 Section 21.9's Elapsed Time option -- how long, in hundredths of a second,
    ///     the client has been trying to complete the current message exchange.
    /// </summary>
    ElapsedTime = 8,

    /// <summary>
    ///     RFC 8415 Section 21.15's User Class option -- one or more "user-class-data" instances
    ///     packed back-to-back, each its own 2-octet length followed by that many octets of opaque
    ///     user class information, the DHCPv6 analogue of RFC 3004's DHCPv4 User Class option
    ///     (<see cref="Dhcpv4OptionTagEnum.UserClass" />) -- structurally identical in spirit (a
    ///     client-chosen, opaque classification string a server may match against), differing only
    ///     in the length-prefix field's own width (2 octets here, versus DHCPv4's 1 octet).
    /// </summary>
    UserClass = 15,

    /// <summary>
    ///     RFC 8415 Section 21.16's Vendor Class option -- a 4-octet IANA Enterprise Number
    ///     followed by one or more "vendor-class-data" instances, each its own 2-octet length
    ///     followed by that many octets of opaque vendor classification information. Structurally
    ///     different from DHCPv4's own single-opaque-string Vendor Class Identifier option
    ///     (<see cref="Dhcpv4OptionTagEnum.VendorClassIdentifier" />, RFC 2132 Section 9.13): a
    ///     DHCPv6 client always identifies its own vendor's IANA Enterprise Number explicitly,
    ///     rather than folding that information into the opaque string itself.
    /// </summary>
    VendorClass = 16,

    /// <summary>
    ///     RFC 8415 Section 21.13's Status Code option -- a numeric result code
    ///     (<see cref="Dhcpv6StatusCodeEnum" />) plus an optional human-readable message, nested
    ///     inside an IA_NA/IA_TA/IA_PD option to report success/failure for that specific identity
    ///     association, or carried at the top level of a message to report success/failure for
    ///     the whole exchange.
    /// </summary>
    StatusCode = 13,

    /// <summary>
    ///     RFC 8415 Section 21.14's Rapid Commit option -- a zero-length presence marker a client
    ///     includes in a SOLICIT to ask a server to reply immediately with a REPLY (skipping the
    ///     ADVERTISE/REQUEST round trip), and a server includes in its own REPLY to confirm it did
    ///     so.
    /// </summary>
    RapidCommit = 14,

    /// <summary>
    ///     RFC 3646 Section 3's DNS Recursive Name Server option -- one or more recursive DNS
    ///     server addresses, in the server's own order of preference, the DHCPv6 analogue of
    ///     DHCPv4's Domain Name Server option (<see cref="Dhcpv4OptionTagEnum.DomainNameServer" />).
    /// </summary>
    DnsRecursiveNameServer = 23,

    /// <summary>
    ///     RFC 3646 Section 3.1's Domain Search List option -- one or more domain names, encoded
    ///     as ordinary uncompressed DNS wire-format names packed back-to-back, for a client to
    ///     search when resolving an unqualified host name.
    /// </summary>
    DomainSearchList = 24,

    /// <summary>
    ///     RFC 8415 Section 21.21's Identity Association for Prefix Delegation (IA_PD) option --
    ///     the Prefix Delegation analogue of <see cref="IaNa" />: a container carrying one
    ///     client-chosen IAID, T1/T2 renewal timers, and a nested options area of one or more
    ///     <see cref="IaPrefix" /> options. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationForPrefixDelegation" />.
    /// </summary>
    IaPd = 25,

    /// <summary>
    ///     RFC 8415 Section 21.22's IA Prefix (IAPREFIX) option -- one specific delegated prefix
    ///     plus its own preferred/valid lifetimes, nested inside an <see cref="IaPd" /> option's
    ///     own options area, the Prefix Delegation analogue of <see cref="IaAddress" />. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6IdentityAssociationPrefix" />.
    /// </summary>
    IaPrefix = 26,

    /// <summary>
    ///     RFC 8415 Section 21.18's Interface-ID option -- opaque bytes a relay agent sets on a
    ///     RELAY-FORW to identify which of its own interfaces a client's message arrived on, that a
    ///     server (or a further relay agent) must echo back completely unchanged on the
    ///     corresponding RELAY-REPL, per RFC 8415 Section 16. This project treats the value as an
    ///     opaque byte string, the same way a DUID is treated -- nothing in this project's own
    ///     relay logic needs to interpret its contents, only echo it back faithfully.
    /// </summary>
    InterfaceId = 18,

    /// <summary>
    ///     RFC 4649's Relay Agent Remote-ID option (OPTION_REMOTE_ID) -- a 4-octet IANA Enterprise
    ///     Number followed by an opaque "remote-id" value a relay agent chooses to identify the
    ///     client (or its attachment point) across a link, inserted into a RELAY-FORW message's own
    ///     options area alongside (never inside) the Relay Message option, the DHCPv6 analogue of
    ///     RFC 3046's DHCPv4 Agent Remote ID sub-option. This project treats the whole option value
    ///     (enterprise number and remote-id together) as one opaque byte string, the same way a DUID
    ///     is treated -- nothing in this project's own wire/transport layer needs to parse the two
    ///     fields apart.
    /// </summary>
    RemoteId = 37,

    /// <summary>
    ///     RFC 4704's Client FQDN option -- a client's own presented fully-qualified (or partial)
    ///     domain name, plus the N/O/S flag bits governing which side (client or server) is
    ///     responsible for the corresponding AAAA/PTR DNS updates. See
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv6MessageOptionExtensions.TryGetClientFqdn" />'s
    ///     own doc comment for the exact bit semantics, quoted directly from RFC 4704 Section 4.1.
    /// </summary>
    ClientFqdn = 39,
}
