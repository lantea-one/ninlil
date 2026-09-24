using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This static class is the read-only, typed metadata catalog of DHCPv4 options this project
///     knows about -- one <see cref="DhcpOptionCatalogEntry" /> per option, sourced directly from
///     the IANA BOOTP/DHCP Parameters registry (<c>https://www.iana.org/assignments/bootp-dhcp-parameters/</c>)
///     and RFC 2132's own defining text, per this project's clean-room rule (see <c>NOTICE.md</c>)
///     -- never from Technitium's or dnsmasq's own code or documentation, even indirectly.
///     <para>
///         <b>Scope</b>: RFC 2132 defines well over 60 options; this catalog deliberately covers a
///         pragmatic subset a real home/homelab network operator would plausibly want to
///         hand-configure, not the whole registry. Excluded, by deliberate choice: every option
///         this project already computes and delivers automatically through a scope's own
///         first-class fields with no custom-option detour needed (<c>DhcpScopeEntity.GatewayAddresses</c>/
///         <c>DnsServerAddresses</c>/<c>SubnetPrefixLength</c> are still included here anyway,
///         matching <c>DhcpScopeCustomOptionEntity</c>'s own documented "a custom option always
///         wins over the standard-computed value" precedent -- an operator can legitimately want to
///         override one); every option that is inherently client-to-server-only information a
///         server never delivers a value for (Parameter Request List, Client-identifier, Vendor
///         Class Identifier, User Class, Requested IP Address); every option that is purely
///         protocol mechanics, already computed/negotiated automatically and never a value an
///         operator configures (DHCP Message Type, Server Identifier, Message, Maximum DHCP Message
///         Size, Option Overload, Relay Agent Information); and a long tail of options this RFC
///         defines for protocols/deployment shapes with no genuine remaining relevance to a modern
///         home/homelab network -- 1980s/1990s diskless-workstation-era services (Boot File Size,
///         Merit Dump File, Swap Server, Extensions Path, X Window System Font Server/Display
///         Manager), legacy application-layer service discovery long since replaced by DNS-based
///         discovery or simply abandoned (Time Server, IEN-116 Name Server, Cookie Server, LPR
///         Server, Impress Server, Resource Location Server, SUN NIS Domain/Servers), obsolete
///         ICMP-era host-autoconfiguration flags DHCP's own Router option superseded decades ago
///         (Non-Local Source Routing, Policy Filter, All Subnets Are Local, Perform Mask
///         Discovery/Mask Supplier, Perform Router Discovery, Router Solicitation Address, Trailer/
///         Ethernet Encapsulation), deprecated Path MTU Discovery tuning (Path MTU Aging
///         Timeout/Plateau Table), and TCP/ARP stack tuning essentially never set via DHCP in
///         practice (Maximum Datagram Reassembly Size, Default IP Time-to-live's TCP-adjacent
///         siblings ARP Cache Timeout/TCP Default TTL/TCP Keepalive Interval/TCP Keepalive
///         Garbage). None of this is a judgment that these options are invalid or never used
///         anywhere -- only that they fall outside this catalog's own stated pragmatic-homelab
///         scope; RFC 2132 §8.4's own site-specific/vendor-extension range (tags 128-254) is
///         likewise excluded wholesale, since IANA itself assigns no fixed name/shape to any tag
///         in that range for this catalog to describe. One further named exclusion worth calling
///         out explicitly: the informally-known "WPAD" proxy-autoconfiguration option (commonly
///         tag 252 in real-world DHCP server implementations) is <b>not</b> an IANA-assigned or
///         RFC-defined option at all -- tag 252 falls inside RFC 2132 §8.4's own site-specific
///         range, and its "WPAD" meaning is purely a widely-adopted implementation convention, not
///         a real option number this catalog's own IANA/RFC sourcing rule can cite. It is excluded
///         for exactly that reason, not overlooked.
///     </para>
///     <para>
///         <b>"Server-address-eligible" reasoning</b> (see <see cref="DhcpOptionCatalogEntry.IsServerAddressEligible" />):
///         marked <see langword="true" /> only for an address-shaped option whose value is
///         plausibly this same box's own address in a real homelab deployment -- Domain Name
///         Server (this project <i>is</i> a DNS server), TFTP Server Name (this project's own
///         <c>Ninlil.Dhcp.Server.Tftp</c> genuinely runs a TFTP server), and Network Time Protocol
///         Servers (a homelab operator who also runs an NTP daemon, e.g. <c>chrony</c>, on this
///         same host plausibly wants to advertise it the same way, even though Ninlil itself does
///         not implement NTP). Every other address-shaped option here (Subnet Mask, Router,
///         Broadcast Address, Static Route, the two NetBIOS server options) is deliberately left
///         <see langword="false" /> -- a subnet mask/broadcast address is network-derived, not a
///         server identity; a router/gateway is conventionally a distinct device from this DNS/DHCP
///         server; and this project implements neither NetBIOS Name Service nor Datagram
///         Distribution, so offering its own address for either would be actively misleading.
///     </para>
/// </summary>
public static class Dhcpv4OptionCatalog
{
    /// <summary>
    ///     This property contains every DHCPv4 option this catalog knows about, in ascending tag
    ///     order.
    /// </summary>
    public static IReadOnlyList<DhcpOptionCatalogEntry> Entries { get; } =
    [
        // RFC 2132 §3.3 -- computed automatically from DhcpScopeEntity.SubnetPrefixLength, but
        // a custom option for this tag can still override it per DhcpScopeCustomOptionEntity's
        // own documented precedence rule.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.SubnetMask,
            "Subnet Mask",
            "RFC 2132 §3.3 -- the client's own subnet mask, as a dotted-quad IPv4 address.",
            DhcpOptionValueTypeEnum.IPv4Address,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.5 -- computed automatically from DhcpScopeEntity.GatewayAddresses, same
        // override precedence as above.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.Router,
            "Router",
            "RFC 2132 §3.5 -- one or more router (default gateway) addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.8 -- computed automatically from DhcpScopeEntity.DnsServerAddresses, same
        // override precedence as above. Server-address-eligible: this project is itself a DNS
        // server.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.DomainNameServer,
            "Domain Name Server",
            "RFC 2132 §3.8 -- one or more recursive DNS server addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: true
        ),
        // RFC 2132 §3.14.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.HostName,
            "Host Name",
            "RFC 2132 §3.14 -- the client's own host name, as an NVT ASCII string.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.17.
        new DhcpOptionCatalogEntry(
            15,
            "Domain Name",
            "RFC 2132 §3.17 -- the domain name the client should use when resolving host names via DNS.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.19.
        new DhcpOptionCatalogEntry(
            17,
            "Root Path",
            "RFC 2132 §3.19 -- the path name of the client's own root disk (an NFS-mounted root, most commonly for a diskless/PXE client).",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §4.1.
        new DhcpOptionCatalogEntry(
            19,
            "IP Forwarding Enable/Disable",
            "RFC 2132 §4.1 -- whether the client should configure IP forwarding (packet routing between its own interfaces).",
            DhcpOptionValueTypeEnum.Boolean,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §4.5.
        new DhcpOptionCatalogEntry(
            23,
            "Default IP Time-to-live",
            "RFC 2132 §4.5 -- the default time-to-live the client should use on outgoing IP packets.",
            DhcpOptionValueTypeEnum.UInt8,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §5.1.
        new DhcpOptionCatalogEntry(
            26,
            "Interface MTU",
            "RFC 2132 §5.1 -- the MTU the client should use on the interface this lease was drawn on.",
            DhcpOptionValueTypeEnum.UInt16,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §5.3.
        new DhcpOptionCatalogEntry(
            28,
            "Broadcast Address",
            "RFC 2132 §5.3 -- the broadcast address in use on the client's own subnet.",
            DhcpOptionValueTypeEnum.IPv4Address,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §5.8 -- a real wire shape this catalog's own vocabulary does not decompose:
        // one or more (destination, router) address pairs packed back-to-back. Recorded as
        // opaque; a future delivery implementation is expected to give it its own dedicated
        // structured editor rather than forcing it through this catalog's plain address-list
        // shape.
        new DhcpOptionCatalogEntry(
            33,
            "Static Route",
            "RFC 2132 §5.8 -- a list of (destination address, router address) pairs describing static routes the client should install.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.3. Server-address-eligible: a homelab operator running an NTP daemon
        // (e.g. chrony) on this same host may plausibly want to advertise it here, even though
        // Ninlil itself implements no NTP service of its own.
        new DhcpOptionCatalogEntry(
            42,
            "Network Time Protocol Servers",
            "RFC 2132 §8.3 -- one or more NTP server addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: true
        ),
        // RFC 2132 §8.4 -- genuinely unstructured by this RFC's own definition (an opaque
        // vendor-defined byte string).
        new DhcpOptionCatalogEntry(
            43,
            "Vendor Specific Information",
            "RFC 2132 §8.4 -- opaque, vendor-defined binary information.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.5. Not server-address-eligible: this project implements no NetBIOS Name
        // Service of its own.
        new DhcpOptionCatalogEntry(
            44,
            "NetBIOS over TCP/IP Name Server",
            "RFC 2132 §8.5 -- one or more NetBIOS Name Server (NBNS/WINS) addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.6. Not server-address-eligible: this project implements no NetBIOS
        // Datagram Distribution Service of its own.
        new DhcpOptionCatalogEntry(
            45,
            "NetBIOS over TCP/IP Datagram Distribution Server",
            "RFC 2132 §8.6 -- one or more NetBIOS Datagram Distribution Server (NBDD) addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.7.
        new DhcpOptionCatalogEntry(
            46,
            "NetBIOS over TCP/IP Node Type",
            "RFC 2132 §8.7 -- the NetBIOS node type the client should use (b-node/p-node/m-node/h-node).",
            DhcpOptionValueTypeEnum.UInt8,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.8.
        new DhcpOptionCatalogEntry(
            47,
            "NetBIOS over TCP/IP Scope",
            "RFC 2132 §8.8 -- the NetBIOS over TCP/IP scope parameter for this client.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §9.2 -- computed automatically from DhcpScopeEntity.DefaultLeaseDuration/
        // MaxLeaseDuration, same override precedence as Subnet Mask/Router/Domain Name Server
        // above.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.IpAddressLeaseTime,
            "IP Address Lease Time",
            "RFC 2132 §9.2 -- the duration, in seconds, of the granted lease.",
            DhcpOptionValueTypeEnum.Duration,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §9.11.
        new DhcpOptionCatalogEntry(
            58,
            "Renewal (T1) Time Value",
            "RFC 2132 §9.11 -- the duration, in seconds, from address assignment until the client transitions to the RENEWING state.",
            DhcpOptionValueTypeEnum.Duration,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §9.12.
        new DhcpOptionCatalogEntry(
            59,
            "Rebinding (T2) Time Value",
            "RFC 2132 §9.12 -- the duration, in seconds, from address assignment until the client transitions to the REBINDING state.",
            DhcpOptionValueTypeEnum.Duration,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §9.4. Server-address-eligible: this project's own Ninlil.Dhcp.Server.Tftp
        // genuinely runs a real TFTP server -- unlike Domain Name Server/NTP Servers above,
        // this is modeled as a single address (RFC 2132 describes it as "a string", but this
        // project's own Dhcpv4OptionDeliveryService already always populates it from a single
        // resolved IPv4Address, matching real-world usage).
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.TftpServerName,
            "TFTP Server Name",
            "RFC 2132 §9.4 -- an alternative, PRL-gated way to convey the TFTP server a PXE client should fetch its boot file from.",
            DhcpOptionValueTypeEnum.IPv4Address,
            IsServerAddressEligible: true
        ),
        // RFC 2132 §9.5.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.BootfileName,
            "Bootfile Name",
            "RFC 2132 §9.5 -- the same alternative, PRL-gated delivery Tftp Server Name documents, for the boot file's own name.",
            DhcpOptionValueTypeEnum.String,
            IsServerAddressEligible: false
        ),
    ];
}
