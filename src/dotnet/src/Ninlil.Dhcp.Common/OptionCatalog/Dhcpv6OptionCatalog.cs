using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This static class is the read-only metadata catalog of DHCPv6 options, the counterpart of
///     <see cref="Dhcpv4OptionCatalog" />. It is written from the IANA DHCPv6 Option Codes registry
///     and the text of RFC 8415, RFC 3646 and RFC 5970, under the clean-room rule in
///     <c>NOTICE.md</c>.
///     <para>
///         <b>Scope.</b> The list is short. Most RFC 8415 options are protocol mechanics with
///         their own wire types (the IA options, IA Address and IA Prefix, Status Code, Rapid
///         Commit, Elapsed Time, Option Request, Preference, Client and Server Identifier, Relay
///         Message, Interface-ID, Remote-ID), or information only clients send (Client FQDN, User
///         Class, Vendor Class); an operator never configures those as custom options. DNS servers
///         also have a dedicated scope field (<c>DhcpScopeEntity.DnsServerAddresses</c>). The SIP,
///         NIS and SNTP server options (RFC 3319, RFC 3898, RFC 4075) are left out as irrelevant to
///         a home or homelab network, as their DHCPv4 counterparts are. The RFC 5908 NTP Server
///         option (code 56) is left out because its value is a list of typed sub-options that the
///         value types here cannot represent, and forcing it into
///         <see cref="DhcpOptionValueTypeEnum.OpaqueHex" /> would hide that structure.
///     </para>
///     <para>
///         <b>Server-address eligibility.</b> As in <see cref="Dhcpv4OptionCatalog" />, only DNS
///         Recursive Name Server is eligible, since Ninlil is a DNS server. Domain Search List,
///         Bootfile URL and Bootfile Parameters are not address-shaped, and "this server's
///         address" means nothing for the vendor-defined Vendor-specific Information.
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
        // RFC 8415 §21.17: a 4-octet IANA Enterprise Number followed by a nested
        // code/length/data list, the counterpart of the DHCPv4 Vendor Specific Information option.
        new DhcpOptionCatalogEntry(
            17,
            "Vendor-specific Information",
            "RFC 8415 §21.17 -- a 4-octet IANA Enterprise Number followed by opaque, vendor-defined encapsulated option data.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 3646 §3: normally filled from DhcpScopeEntity.DnsServerAddresses; a custom option
        // with this code overrides it (see DhcpScopeCustomOptionEntity). Server-address eligible
        // because Ninlil is a DNS server.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv6OptionCodeEnum.DnsRecursiveNameServer,
            "DNS Recursive Name Server",
            "RFC 3646 §3 -- one or more recursive DNS server addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv6AddressList,
            IsServerAddressEligible: true
        ),
        // RFC 3646: a list of domain names, sent on the wire as uncompressed RFC 1035 §3.1 names
        // back to back. Tagged as String (see DhcpOptionValueTypeEnum.String), which does not
        // produce that encoding.
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
        // RFC 5970 §3.1: the counterpart of the DHCPv4 Bootfile Name option, carrying a URI
        // rather than a file name.
        new DhcpOptionCatalogEntry(
            59,
            "Bootfile URL",
            "RFC 5970 §3.1 -- the URI of the boot file a netboot/PXE client should fetch.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 5970 §3.2: one or more length-prefixed parameter strings for the boot file named
        // by Bootfile URL, entered as raw hex.
        new DhcpOptionCatalogEntry(
            60,
            "Bootfile Parameters",
            "RFC 5970 §3.2 -- one or more opaque parameters passed to the boot file named by the Bootfile URL option.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
    ];
}
