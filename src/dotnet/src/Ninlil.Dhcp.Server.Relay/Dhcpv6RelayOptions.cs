// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class binds to the <c>DhcpServer:Relay:Dhcpv6</c> section of the application's
///     configuration and controls whether this server acts as an RFC 8415 DHCPv6 relay agent --
///     forwarding a client's message to one or more configured upstream DHCPv6 servers, wrapped in
///     a RELAY-FORW envelope, rather than answering it itself from local scope/lease state -- and,
///     if so, how. This project's own DHCPv6 relay support is scoped to a single relay hop; see
///     <c>Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessage</c>'s own doc comment for why.
///     <para>
///         <b>This is mutually exclusive with <c>Ninlil.Dhcp.Server.Core</c>'s own local DHCPv6
///         engine, by design</b> -- the exact same "a real host does not, on the same listening
///         instance, both answer authoritatively AND forward to someone else for the same
///         request" reasoning <c>Dhcpv4RelayOptions</c>'s own doc comment documents for DHCPv4.
///         The composition root (<c>Program.cs</c>) reads this class's own <see cref="Enable" />
///         flag directly, before dependency injection is built, and registers exactly one
///         <c>IDhcpv6RequestHandler</c>/<c>IDhcpv6RelayMessageHandler</c> pair accordingly --
///         mirroring the DHCPv4 relay-vs-local decision exactly, just made independently for
///         DHCPv6's own, separate engine.
///     </para>
/// </summary>
public sealed class Dhcpv6RelayOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Relay:Dhcpv6";

    /// <summary>
    ///     This property controls whether this server acts as a DHCPv6 relay agent at all. Off by
    ///     default -- the same "capability-expanding toggles default off" reasoning
    ///     <c>Dhcpv4RelayOptions.Enable</c>'s own doc comment already documents for the DHCPv4
    ///     relay agent applies here too.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the IPv6 addresses of every upstream DHCPv6 server this relay
    ///     forwards a client's message to, wrapped in a RELAY-FORW envelope. A real relay agent
    ///     conventionally forwards to every configured server at once (whichever one actually
    ///     replies first wins) rather than picking just one -- this project follows the same
    ///     convention its own DHCPv4 relay agent already establishes. Left empty (the default), a
    ///     message this relay would otherwise forward is simply dropped, with a warning logged once
    ///     per occurrence, rather than this server binding sockets with genuinely nothing useful to
    ///     do with what they receive.
    /// </summary>
    public List<string> UpstreamServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the UDP port every configured upstream DHCPv6 server listens on
    ///     for a forwarded RELAY-FORW message. 547 is the IANA-assigned DHCPv6 server port, and the
    ///     overwhelmingly common real-world value -- the same port a DHCPv6 relay agent or server
    ///     always uses for message exchange with another relay agent or server, per RFC 8415
    ///     Section 7.1.
    /// </summary>
    public int UpstreamServerPort { get; set; } = 547;

    /// <summary>
    ///     This property optionally contains the IPv6 address this relay identifies itself with
    ///     via the RFC 8415 Section 7.1 "link-address" field of every RELAY-FORW it sends -- a
    ///     global address (never link-local or loopback -- RFC 8415 explicitly forbids either)
    ///     identifying which of this relay's own client-facing links a request arrived on, which a
    ///     server uses to select the correct scope. Left <see langword="null" /> (the default),
    ///     this is auto-detected once, at startup, the first non-loopback, non-link-local IPv6
    ///     unicast address found on any network interface -- the same auto-detection precedent
    ///     <c>Dhcpv4RelayOptions.RelayAgentAddress</c>'s own doc comment describes for
    ///     <c>giaddr</c>, and for the exact same underlying reason: this project's transport binds
    ///     a single wildcard socket with no per-packet receiving-interface information to fall back
    ///     on, so a genuinely multi-homed, multi-subnet relay deployment needs this set explicitly.
    /// </summary>
    public string RelayLinkAddress { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 8415 Section 21.18 Interface-ID text (as an
    ///     opaque ASCII string) this relay adds to every RELAY-FORW it sends, letting its own
    ///     upstream server tell which of this relay's own interfaces a client's message arrived on
    ///     -- the DHCPv6 analogue of <c>Dhcpv4RelayOptions.RelayAgentCircuitId</c>. Left
    ///     <see langword="null" /> or empty (the default), no Interface-ID option is added.
    /// </summary>
    public string InterfaceId { get; set; }
}
