using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This static class is the read-only metadata catalog of DHCPv4 options, one
///     <see cref="DhcpOptionCatalogEntry" /> per option. It is written from the IANA BOOTP/DHCP
///     Parameters registry (<c>https://www.iana.org/assignments/bootp-dhcp-parameters/</c>) and
///     the RFC 2132 text, under the clean-room rule in <c>NOTICE.md</c>, never from Technitium's
///     or dnsmasq's code or documentation.
///     <para>
///         <b>Scope.</b> RFC 2132 defines over 60 options; this catalog covers the ones a home or
///         homelab operator is likely to set by hand. Subnet Mask, Router, Domain Name Server and
///         IP Address Lease Time are normally filled from scope fields, but are listed because a
///         custom option overrides the computed value (see <c>DhcpScopeCustomOptionEntity</c>).
///         Left out:
///         <list type="bullet">
///             <item>
///                 Options only clients send (Parameter Request List, Client-identifier, Vendor
///                 Class Identifier, User Class, Requested IP Address).
///             </item>
///             <item>
///                 Protocol mechanics the server negotiates itself (DHCP Message Type, Server
///                 Identifier, Message, Maximum DHCP Message Size, Option Overload, Relay Agent
///                 Information).
///             </item>
///             <item>
///                 Options with no place on a modern home network: diskless-workstation services
///                 (Boot File Size, Merit Dump File, Swap Server, Extensions Path, X Window System
///                 Font Server and Display Manager), service discovery long replaced or abandoned
///                 (Time Server, IEN-116 Name Server, Cookie Server, LPR Server, Impress Server,
///                 Resource Location Server, SUN NIS Domain and Servers), host flags the Router
///                 option superseded (Non-Local Source Routing, Policy Filter, All Subnets Are
///                 Local, Perform Mask Discovery, Mask Supplier, Perform Router Discovery, Router
///                 Solicitation Address, Trailer and Ethernet Encapsulation), Path MTU Aging
///                 Timeout and Plateau Table, and TCP/ARP tuning rarely set by DHCP (Maximum
///                 Datagram Reassembly Size, ARP Cache Timeout, TCP Default TTL, TCP Keepalive
///                 Interval and Garbage).
///             </item>
///             <item>
///                 The RFC 2132 §8.4 site-specific range (tags 128-254), which has no IANA names
///                 or shapes. This includes the "WPAD" option that many servers put at tag 252: it
///                 is a convention, not an IANA or RFC option.
///             </item>
///         </list>
///         Leaving an option out is a scope choice, not a claim that it is invalid. Any tag can
///         still be configured as a raw custom option.
///     </para>
///     <para>
///         <b>Server-address eligibility</b> (see <see cref="DhcpOptionCatalogEntry.IsServerAddressEligible" />)
///         is set only for address-shaped options whose value is plausibly this host: Domain Name
///         Server (Ninlil is a DNS server), TFTP Server Name (<c>Ninlil.Dhcp.Server.Tftp</c> runs a
///         TFTP server), and Network Time Protocol Servers (an operator may run an NTP daemon such
///         as <c>chrony</c> on the same host, although Ninlil has no NTP service). Subnet Mask and
///         Broadcast Address are network values, not a server identity; the router is usually a
///         different device; and Ninlil has no NetBIOS name or datagram service, so offering its
///         address for those options would mislead.
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
        // RFC 2132 §3.3: normally filled from DhcpScopeEntity.SubnetPrefixLength; a custom option
        // with this tag overrides it (see DhcpScopeCustomOptionEntity).
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.SubnetMask,
            "Subnet Mask",
            "RFC 2132 §3.3 -- the client's own subnet mask, as a dotted-quad IPv4 address.",
            DhcpOptionValueTypeEnum.IPv4Address,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.5: normally filled from DhcpScopeEntity.GatewayAddresses; overridable as
        // above.
        new DhcpOptionCatalogEntry(
            (int)Dhcpv4OptionTagEnum.Router,
            "Router",
            "RFC 2132 §3.5 -- one or more router (default gateway) addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §3.8: normally filled from DhcpScopeEntity.DnsServerAddresses; overridable as
        // above. Server-address eligible because Ninlil is a DNS server.
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
        // RFC 2132 §5.8: one or more (destination, router) address pairs back to back. The value
        // types here cannot describe pairs, so it is entered as raw hex.
        new DhcpOptionCatalogEntry(
            33,
            "Static Route",
            "RFC 2132 §5.8 -- a list of (destination address, router address) pairs describing static routes the client should install.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.3. Server-address eligible: an operator running an NTP daemon (e.g.
        // chrony) on this host may want to advertise it, although Ninlil has no NTP service.
        new DhcpOptionCatalogEntry(
            42,
            "Network Time Protocol Servers",
            "RFC 2132 §8.3 -- one or more NTP server addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: true
        ),
        // RFC 2132 §8.4: an opaque, vendor-defined byte string.
        new DhcpOptionCatalogEntry(
            43,
            "Vendor Specific Information",
            "RFC 2132 §8.4 -- opaque, vendor-defined binary information.",
            DhcpOptionValueTypeEnum.OpaqueHex,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.5. Not server-address eligible: Ninlil has no NetBIOS Name Service.
        new DhcpOptionCatalogEntry(
            44,
            "NetBIOS over TCP/IP Name Server",
            "RFC 2132 §8.5 -- one or more NetBIOS Name Server (NBNS/WINS) addresses, in order of preference.",
            DhcpOptionValueTypeEnum.IPv4AddressList,
            IsServerAddressEligible: false
        ),
        // RFC 2132 §8.6. Not server-address eligible: Ninlil has no NetBIOS Datagram
        // Distribution Service.
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
        // RFC 2132 §9.2: normally computed from DhcpScopeEntity.DefaultLeaseDuration and
        // MaxLeaseDuration; overridable like Subnet Mask, Router and Domain Name Server.
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
        // RFC 2132 §9.4. Server-address eligible: Ninlil.Dhcp.Server.Tftp runs a TFTP server.
        // Modeled as a single IPv4 address, although RFC 2132 defines the value as a string and
        // Dhcpv4OptionDeliveryService sends the address in text form; a value entered or
        // substituted through this entry is sent as 4 binary octets.
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
