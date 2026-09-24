using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This static class is the read-only, typed metadata catalog of DHCPv6 options this project
///     knows about -- the DHCPv6 analogue of <see cref="Dhcpv4OptionCatalog" />, sourced directly
///     from the IANA BOOTP/DHCP Parameters registry's own DHCPv6 Option Codes table and RFC 8415/
///     RFC 3646/RFC 5970's defining text, per this project's clean-room rule (see
///     <c>NOTICE.md</c>).
///     <para>
///         <b>Scope, and why this catalog is genuinely smaller than <see cref="Dhcpv4OptionCatalog" />'s</b>:
///         most of RFC 8415's own option codes are protocol mechanics this project already models
///         as first-class wire types (<c>Dhcpv6IdentityAssociationForNonTemporaryAddresses</c>/
///         <c>...ForTemporaryAddresses</c>/<c>...ForPrefixDelegation</c>, <c>Dhcpv6IdentityAssociationAddress</c>/
///         <c>...Prefix</c>, Status Code, Rapid Commit, Elapsed Time, Option Request, Preference,
///         Client/Server Identifier, Relay Message, Interface-ID, Remote-ID) or inherently
///         client-to-server-only information a server never delivers a value for (Client FQDN, User
///         Class, Vendor Class) -- none of those are a value an operator hand-configures through a
///         scope's own custom-option table, so none appear here, exactly mirroring
///         <see cref="Dhcpv4OptionCatalog" />'s own "client-originated/protocol-mechanic" exclusion
///         reasoning. What remains -- genuine, operator-configurable values a DHCPv6 server
///         delivers to a client, beyond what this project's own dedicated scope fields
///         (<c>DhcpScopeEntity.DnsServerAddresses</c>) already cover -- is a real, but genuinely
///         short, list: RFC 8415/RFC 3646/RFC 5970 simply define far fewer broadly-applicable
///         "site information" options than RFC 2132 does for DHCPv4 (DHCPv6's own further RFCs
///         defining SIP/NIS/SNTP server options -- RFC 3319, RFC 3898, RFC 4075 -- describe
///         deployment shapes with no genuine remaining relevance to a modern home/homelab network,
///         the same reasoning excluding their DHCPv4 equivalents). RFC 5908's own NTP Server option
///         (option code 56) is deliberately excluded for a different reason than relevance: its
///         value is not a flat address list at all, but a sequence of typed sub-options (server
///         address / multicast address / server FQDN) nested one level deep -- a genuinely
///         different shape from anything this catalog's own small value-type vocabulary
///         represents, left for a future sub-phase to model properly rather than forced through
///         <see cref="DhcpOptionValueTypeEnum.OpaqueHex" /> and losing its own real structure
///         entirely.
///     </para>
///     <para>
///         <b>"Server-address-eligible" reasoning</b>: mirrors <see cref="Dhcpv4OptionCatalog" />'s
///         own -- <see langword="true" /> only for DNS Recursive Name Server, since this project is
///         itself a DNS server. Domain Search List/Bootfile URL/Bootfile Parameters are not
///         address-shaped at all (see <see cref="DhcpOptionCatalogEntry.IsServerAddressEligible" />'s
///         own enforced invariant), and Vendor-specific Information's opaque, vendor-defined shape
///         makes "this server's own address" meaningless as a value for it.
///     </para>
/// </summary>
public static class Dhcpv6OptionCatalog
{
    /// <summary>
    ///     This property contains every DHCPv6 option this catalog knows about, in ascending code
    ///     order.
    /// </summary>
    public static IReadOnlyList<DhcpOptionCatalogEntry> Entries { get; } =
    [
        // RFC 8415 §21.17 -- genuinely unstructured by this catalog's own vocabulary: a 4-octet
        // IANA Enterprise Number followed by a nested option-code/length/data list, the DHCPv6
        // analogue of DHCPv4's own Vendor Specific Information option.
        new DhcpOptionCatalogEntry(
            17,
            "Vendor-specific Information",
            "RFC 8415 §21.17 -- a 4-octet IANA Enterprise Number followed by opaque, vendor-defined encapsulated option data.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 3646 §3 -- computed automatically from DhcpScopeEntity.DnsServerAddresses, the
        // same "a custom option for this code can still override the standard-computed value"
        // precedence DhcpScopeCustomOptionEntity's own doc comment establishes for DHCPv4.
        // Server-address-eligible: this project is itself a DNS server.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv6OptionCodeEnum.DnsRecursiveNameServer,
            "DNS Recursive Name Server",
            "RFC 3646 §3 -- one or more recursive DNS server addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv6AddressList,
            IsServerAddressEligible: true
        ),
        // RFC 3646 §3.1 -- a list of domain names, encoded on the wire as ordinary uncompressed
        // RFC 1035 §3.1 names packed back-to-back; categorized as a delimited String here (see
        // DhcpOptionValueTypeEnum.String's own doc comment), deferring the real wire encoding
        // to a future delivery implementation.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv6OptionCodeEnum.DomainSearchList,
            "Domain Search List",
            "RFC 3646 §3.1 -- one or more domain names for a client to search when resolving an unqualified host name.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 8415 §21.23.
        new DhcpOptionCatalogEntry(
            32,
            "Information Refresh Time",
            "RFC 8415 §21.23 -- the duration, in seconds, a stateless client should wait before refreshing information via a new Information-Request.",
            DhcpOptionValueTypeEnum.Duration,
            IsServerAddressEligible: false
        ),
        // RFC 5970 §3.1 -- the DHCPv6 analogue of DHCPv4's Bootfile Name option, but carrying a
        // full URI rather than a bare file name.
        new DhcpOptionCatalogEntry(
            59,
            "Bootfile URL",
            "RFC 5970 §3.1 -- the URI of the boot file a netboot/PXE client should fetch.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 5970 §3.2 -- one or more length-prefixed opaque parameter strings passed to the
        // boot file named by Bootfile URL; not decomposed by this catalog's own vocabulary.
        new DhcpOptionCatalogEntry(
            60,
            "Bootfile Parameters",
            "RFC 5970 §3.2 -- one or more opaque parameters passed to the boot file named by the Bootfile URL option.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
    ];
}
