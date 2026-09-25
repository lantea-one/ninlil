// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverTls:ProxyProtocol</c> section of the
///     application's configuration and controls whether <see cref="DnsOverTlsListener" /> accepts
///     a HAProxy-style PROXY protocol header (v1 text or v2 binary) at the start of a TCP
///     connection, ahead of the TLS handshake. A TCP-level proxy in front of this transport
///     (HAProxy, nginx's <c>stream</c> module, Caddy's <c>layer4</c> app) uses it to pass on the
///     address of the client it relays for. <c>ReverseProxySettingsController</c> writes it.
///     <para>
///         It is a separate allowlist from <c>KnownProxies</c> and <c>KnownNetworks</c> in
///         <c>Ninlil.Web.Options.ReverseProxyOptions</c>: this project cannot reference
///         <c>Ninlil.Web</c> (the dependency runs the other way), and the TCP proxy in front of
///         DNS-over-TLS need not be the HTTP proxy in front of the API, the admin UI and
///         DNS-over-HTTPS, though it often is. Both allowlists have the same shape (addresses plus
///         CIDR networks), so an operator with one proxy repeats the same values in both.
///     </para>
///     <para>
///         It is off by default: honoring a claimed address from any connection would let a
///         client prepend a forged header and pose as another client, so an operator opts in only
///         with a correctly configured proxy in front of the listener.
///     </para>
///     <para>
///         <b>There is no shared secret, unlike the HTTP side.</b> The PROXY protocol header has no
///         place for one short of a custom v2 TLV, which is out of scope. The allowlist is the
///         whole trust boundary: a connection from a source it does not list is never inspected
///         for a header, so a client outside it cannot spoof an address with a forged header, or
///         learn whether the feature is on.
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
    ///     answered using the TCP peer address.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the individual IP addresses (no CIDR notation) allowed to open a
    ///     connection carrying a PROXY protocol header, for a proxy with a fixed address. A
    ///     connection whose peer address matches neither this list nor a network in
    ///     <see cref="KnownNetworks" /> is never inspected for a header.
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>
    ///     This property contains every CIDR network (for example <c>10.0.0.0/24</c>) allowed to
    ///     open a connection carrying a PROXY protocol header, for a proxy whose address is not
    ///     fixed. Entries are parsed with <see cref="System.Net.IPNetwork" />, as
    ///     <c>Ninlil.Web.Options.ReverseProxyOptions.KnownNetworks</c> entries are.
    /// </summary>
    public List<string> KnownNetworks { get; set; } = [];
}
