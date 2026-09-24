// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class binds to the <c>DhcpServer:Core</c> section of the application's configuration
///     and controls behavior shared by every DHCP protocol engine (both DHCPv4 and DHCPv6 read
///     this same section) rather than one specific wire protocol's own transport settings -- those
///     live on each protocol's own options class instead (for example
///     <see cref="Protocol.Dhcpv4.Dhcpv4ProtocolOptions" />).
/// </summary>
public sealed class DhcpServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Core";

    /// <summary>
    ///     This property contains the IPv4 address this server identifies itself as via the
    ///     Server Identifier option (RFC 2132 Section 9.7) in every OFFER/ACK it sends, and the
    ///     value a client's own REQUEST names to confirm which server's offer it is accepting.
    ///     <para>
    ///         Left <see langword="null" /> (the default), this is auto-detected once, at
    ///         startup, as the first non-loopback IPv4 unicast address bound to any active network
    ///         interface on this host -- see <see cref="Identity.DhcpServerIdentifierProvider" />.
    ///         A real DHCP server conventionally identifies itself using the address of whichever
    ///         interface actually received the request, but this project's own
    ///         <see cref="Protocol.Dhcpv4.Dhcpv4UdpListener" /> binds a single wildcard
    ///         (<c>INADDR_ANY</c>) socket and does not capture per-packet interface information
    ///         (that would need <c>IP_PKTINFO</c> and a receive API this project does not use), so
    ///         there is no way to recover "which interface" a given request actually arrived on.
    ///         For this project's own primary deployment target -- a homelab or home router with
    ///         one relevant LAN interface -- a single, explicitly configurable server identifier
    ///         is a deliberate, honest scope decision rather than an oversight: an operator running
    ///         more than one DHCPv4-serving interface with genuinely different subnets should set
    ///         this explicitly to the address of whichever interface should be presented to
    ///         clients, since auto-detection cannot know which one is "correct" for every scope.
    ///     </para>
    /// </summary>
    public string ServerIdentifierAddress { get; set; }

    /// <summary>
    ///     This property contains this server's own DHCPv6 DUID (DHCP Unique Identifier, RFC 8415
    ///     Section 11), as a colon-separated or plain hexadecimal string -- the DHCPv6 analogue of
    ///     <see cref="ServerIdentifierAddress" />, carried in every reply's own Server Identifier
    ///     option (<c>Dhcpv6OptionCodeEnum.ServerIdentifier</c>) and compared against the DUID a
    ///     client's own REQUEST/RENEW names to confirm which server it is addressing.
    ///     <para>
    ///         Left <see langword="null" /> (the default), this is derived once, at startup, as a
    ///         DUID-LL (RFC 8415 Section 11.2) built from the link-layer (MAC) address of the same
    ///         network interface <see cref="Identity.DhcpServerIdentifierProvider" /> would pick
    ///         for <see cref="ServerIdentifierAddress" /> -- see
    ///         <see cref="Identity.Dhcpv6ServerIdentifierProvider" />. Unlike an IPv4 address, a
    ///         DUID-LL needs no persistence to stay stable across a restart: it is entirely
    ///         determined by that interface's own hardware address, which does not change on its
    ///         own, so the exact same value is derived again every time this process starts, with
    ///         no config-file or database bookkeeping needed to remember it.
    ///     </para>
    /// </summary>
    public string ServerDuid { get; set; }

    /// <summary>
    ///     This property contains this server's own real IPv6 address -- entirely distinct from
    ///     <see cref="ServerDuid" />, which is an opaque protocol identifier, never a usable
    ///     network address. Substituted at delivery time for any configured DHCPv6 custom/
    ///     tag-rule option whose own <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum" />
    ///     is <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" /> --
    ///     see <see cref="Identity.Dhcpv6ServerAddressProvider" />.
    ///     <para>
    ///         Left <see langword="null" /> (the default), this is auto-detected once, at
    ///         startup, as the first non-loopback, non-link-local IPv6 unicast address bound to
    ///         any active network interface on this host -- the same auto-detection shape
    ///         <see cref="ServerIdentifierAddress" /> already establishes for IPv4, and the same
    ///         single-interface deployment-scope reasoning that property's own doc comment already
    ///         documents applies here too: an operator running more than one IPv6-serving
    ///         interface with genuinely different addresses should set this explicitly.
    ///     </para>
    /// </summary>
    public string ServerIPv6Address { get; set; }

    /// <summary>
    ///     This property controls whether this server ever commits a DHCPv6 lease directly in
    ///     response to a SOLICIT that itself carries a Rapid Commit option (RFC 8415 Section
    ///     18.3.9/21.14), skipping the ordinary ADVERTISE/REQUEST round trip and replying with a
    ///     committed REPLY immediately. Off by default: RFC 8415 Section 18.3.9 itself warns that
    ///     rapid commit should only be enabled "if the administrator of the DHCP server is
    ///     confident" no other DHCPv6 server on the same link might also commit a conflicting
    ///     lease for the same client -- a real, if narrow, correctness risk in a genuinely
    ///     multi-server deployment that this project's own "capability-expanding toggles default
    ///     off" invariant applies to directly, even though this project's own primary deployment
    ///     target (a single-server homelab) is exactly the case where turning it on is safe.
    /// </summary>
    public bool EnableDhcpv6RapidCommit { get; set; }

    /// <summary>
    ///     This property contains how long a tentatively offered address (a lease left in the
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" /> state after a DISCOVER) is
    ///     held aside for the client that was offered it before it is treated as abandoned and
    ///     becomes available to a different client again. A well-behaved client sends its REQUEST
    ///     within seconds of receiving an OFFER, so this only matters for a client that never
    ///     follows through (it moved to a different network, chose a different server's offer, or
    ///     simply went away).
    /// </summary>
    public TimeSpan OfferHoldDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how long an address a client DECLINEd (RFC 2131 Section 4.3.3 --
    ///     the client's own ARP probe found the address already in use by something else on the
    ///     network) is kept out of circulation for every client, not only the one that declined
    ///     it, before this server is willing to offer it again. There is no dedicated
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum" /> member for "declined" -- see
    ///     <see cref="Allocation.Dhcpv4LeaseAllocationService" />'s own doc comment for how this
    ///     project represents that quarantine using the existing enum's <c>Expired</c> member
    ///     instead of adding a new one to a shared, cross-protocol enum for a single call site's
    ///     benefit.
    /// </summary>
    public TimeSpan DeclinedAddressQuarantineDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     This property contains how often <see cref="Dhcpv4LeaseExpirySweepService" /> checks
    ///     every currently-<see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> lease for
    ///     one whose own <c>LeaseExpiresAtUtc</c> has passed, marking it
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Expired" /> and publishing
    ///     <see cref="Ninlil.Dns.Server.Core.Events.DhcpLeaseChangedEvent" /> for it -- the one
    ///     mechanism that ever detects a lease's own natural expiry, since neither DHCPv4 nor
    ///     DHCPv6 has a wire message a client sends to announce it. A relatively short default
    ///     keeps a departed client's own DNS registration (see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>) from lingering
    ///     long after its lease has genuinely lapsed.
    /// </summary>
    public TimeSpan LeaseExpirySweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
