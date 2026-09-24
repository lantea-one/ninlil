// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverTls:ProxyProtocol</c> section of the
///     application's configuration and controls whether <see cref="DnsOverTlsListener" /> accepts
///     a HAProxy-style PROXY protocol header (v1 text or v2 binary) at the very start of a TCP
///     connection, ahead of the TLS handshake, so a TCP-level proxy (HAProxy, nginx's
///     <c>stream</c> module, Caddy's <c>layer4</c> app) sitting in front of this transport can hand
///     off the real client address it is relaying on behalf of.
///     <para>
///         This is a deliberately separate allowlist from
///         <c>Ninlil.Web.Options.ReverseProxyOptions</c>'s own <c>KnownProxies</c>/
///         <c>KnownNetworks</c>, not a shared instance of it -- this project has no reference onto
///         <c>Ninlil.Web</c> at all (the dependency runs the other way), and a real
///         deployment's own TCP-mode proxy front for DNS-over-TLS is not guaranteed to be the same
///         upstream as whatever HTTP-mode proxy front sits in front of the REST API/Admin SPA/
///         DNS-over-HTTPS, even though it very often is. The two allowlists are identically
///         *shaped* (a list of individual addresses plus a list of CIDR networks) so an operator
///         configuring both simply repeats the same values in both places when their real proxy
///         genuinely is the same box.
///     </para>
///     <para>
///         Off by default, per this project's own "capability-expanding toggle defaults off"
///         posture -- honoring a claimed source address from an arbitrary connection would let any
///         TCP client simply prepend its own forged PROXY protocol header and make this server
///         believe it is answering a different client than the one it is actually talking to,
///         unless an operator has an actual, correctly-configured TCP-level proxy in front of this
///         listener and deliberately opts in.
///     </para>
///     <para>
///         <b>Unlike the HTTP-side allowlist, there is no optional shared-secret mechanism here at
///         all</b> -- the PROXY protocol's own fixed v1/v2 header framing has no natural place to
///         carry an arbitrary secret without a custom, non-standard v2 TLV extension, which is out
///         of scope. The IP/CIDR allowlist alone is the entire trust boundary: a connection from a
///         source this allowlist does not recognize is never even inspected for a PROXY protocol
///         prefix at all, so a client sitting outside it cannot use a forged header to spoof an
///         address, and cannot even discover whether this feature is enabled.
///     </para>
/// </summary>
public sealed class ProxyProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = DnsOverTlsOptions.SectionName + ":ProxyProtocol";

    /// <summary>
    ///     This property controls whether a connection from a recognized source is inspected for a
    ///     PROXY protocol header at all. When <see langword="false" />, every connection is
    ///     answered using the raw TCP connection's own peer address exactly as it always has been.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains every individual IP address (no CIDR notation) allowed to open a
    ///     connection carrying a PROXY protocol header -- the exact single-address entries a
    ///     single, fixed-address TCP-level proxy in front of this listener needs. Combined with
    ///     <see cref="KnownNetworks" />; a connection whose own peer address matches neither this
    ///     list nor any network in <see cref="KnownNetworks" /> is never inspected for a PROXY
    ///     protocol prefix, regardless of <see cref="Enable" />.
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>
    ///     This property contains every CIDR network (for example <c>10.0.0.0/24</c>) allowed to
    ///     open a connection carrying a PROXY protocol header -- for a TCP-level proxy whose own
    ///     address is not fixed. Parsed the same way
    ///     <c>Ninlil.Web.Options.ReverseProxyOptions.KnownNetworks</c> already parses its
    ///     own entries, via <see cref="System.Net.IPNetwork" />'s own parser.
    /// </summary>
    public List<string> KnownNetworks { get; set; } = [];
}
