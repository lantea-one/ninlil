// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class binds to the <c>DhcpServer:Core</c> section of the application's configuration
///     and holds the settings of the DHCPv4 and DHCPv6 server engines: server identity, lease
///     hold times and the expiry sweep. Transport settings (ports, enable flags) are on each
///     protocol's options class, such as <see cref="Protocol.Dhcpv4.Dhcpv4ProtocolOptions" />.
/// </summary>
public sealed class DhcpServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Core";

    /// <summary>
    ///     This property contains the IPv4 address the server sends as its Server Identifier
    ///     (RFC 2132 Section 9.7) and that a client's REQUEST names to accept this server's offer.
    ///     <para>
    ///         Left <see langword="null" /> (the default), or set to something that is not an IPv4
    ///         address, it is detected once at startup as the first IPv4 address of the first
    ///         interface that is up and not loopback (see
    ///         <see cref="Identity.DhcpServerIdentifierProvider" />). Servers usually use the
    ///         address of the interface the request arrived on, but
    ///         <see cref="Protocol.Dhcpv4.Dhcpv4UdpListener" /> binds one wildcard socket and does
    ///         not read per-packet interface information (that would need <c>IP_PKTINFO</c>), so
    ///         one address serves all requests. That fits a home network with one LAN; with
    ///         several DHCPv4 interfaces, or virtual interfaces such as container bridges that may
    ///         be listed first, set it explicitly.
    ///     </para>
    /// </summary>
    public string ServerIdentifierAddress { get; set; }

    /// <summary>
    ///     This property contains the server's DHCPv6 DUID (DHCP Unique Identifier, RFC 8415
    ///     Section 11) as hex, with or without separators. It is the DHCPv6 counterpart of
    ///     <see cref="ServerIdentifierAddress" />: sent in every reply's Server Identifier option
    ///     and compared with the server DUID a client's REQUEST or RENEW names.
    ///     <para>
    ///         Left <see langword="null" /> (the default), or not valid hex, it is derived once at
    ///         startup as a DUID-LL (RFC 8415 Section 11.4) from the link-layer address of the
    ///         first interface that is up, not loopback and has one (see
    ///         <see cref="Identity.Dhcpv6ServerIdentifierProvider" />); that is not necessarily the
    ///         interface the IPv4 server identifier comes from. A DUID-LL needs no storage: the
    ///         same hardware address gives the same DUID after every restart, as long as the same
    ///         interface is listed first.
    ///     </para>
    /// </summary>
    public string ServerDuid { get; set; }

    /// <summary>
    ///     This property contains the server's IPv6 address, as opposed to <see cref="ServerDuid" />,
    ///     which is an identifier rather than an address. It is substituted for a DHCPv6 tag-rule
    ///     option whose value source is
    ///     <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" /> (see
    ///     <see cref="Identity.Dhcpv6ServerAddressProvider" />).
    ///     <para>
    ///         Left <see langword="null" /> (the default), or not a valid non-link-local IPv6
    ///         address, it is detected once at startup as the first IPv6 address that is neither
    ///         loopback nor link-local on an interface that is up and not loopback, like
    ///         <see cref="ServerIdentifierAddress" />. With several IPv6 interfaces, set it
    ///         explicitly.
    ///     </para>
    /// </summary>
    public string ServerIPv6Address { get; set; }

    /// <summary>
    ///     This property controls whether the server answers a SOLICIT carrying a Rapid Commit
    ///     option (RFC 8415 Section 21.14) with an immediate REPLY that commits the lease,
    ///     skipping ADVERTISE and REQUEST. It is off by default: with more than one DHCPv6 server
    ///     on a link, each may commit a lease for the same client, so RFC 8415 advises enabling it
    ///     only when that cannot happen. On a single-server home network it is safe to turn on.
    /// </summary>
    public bool EnableDhcpv6RapidCommit { get; set; }

    /// <summary>
    ///     This property contains how long an offered address (a lease in the
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" /> state after a DISCOVER or
    ///     SOLICIT) is held for that client before another client can get it. Clients usually
    ///     REQUEST within seconds, so this only matters for one that never follows up (it chose
    ///     another server or went away). The default is 30 seconds.
    /// </summary>
    public TimeSpan OfferHoldDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how long an address a client DECLINEd (RFC 2131 Section 4.3.3:
    ///     the client found it already in use, for example by ARP) is withheld from every client
    ///     before it is offered again. The default is 10 minutes. There is no "declined" lease
    ///     state; the quarantine is stored as <c>Expired</c> with a future expiry (see
    ///     <see cref="Allocation.Dhcpv4LeaseAllocationService" />).
    /// </summary>
    public TimeSpan DeclinedAddressQuarantineDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     This property contains how often <see cref="Dhcpv4LeaseExpirySweepService" /> looks for
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> leases whose
    ///     <c>LeaseExpiresAtUtc</c> has passed, marks them
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Expired" /> and publishes a
    ///     <see cref="Ninlil.Dns.Server.Core.Events.DhcpLeaseChangedEvent" />. No client message
    ///     announces an expiry, so this sweep is how it is noticed. The default of one minute
    ///     keeps a departed client's DNS record (see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>) from lingering.
    /// </summary>
    public TimeSpan LeaseExpirySweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
