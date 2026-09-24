// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:ReverseProxy</c> section of the application's
///     configuration and controls whether this host's own shared Kestrel HTTP/HTTPS listener
///     (the REST API, the Angular admin SPA, the SignalR hub, and DNS-over-HTTPS, all of which
///     share this single listener) trusts a reverse proxy's own <c>X-Forwarded-For</c>/
///     <c>X-Forwarded-Proto</c> headers instead of the raw TCP connection's own peer address.
///     Off by default, per this project's own "capability-expanding toggle defaults off" posture
///     -- honoring a forwarded header at all opens a real spoofing surface (a client simply
///     sending its own <c>X-Forwarded-For</c> header directly, with no proxy in between, could
///     otherwise make this server believe it is answering a different client than the one it is
///     actually talking to) unless an operator has an actual reverse proxy in front of this host
///     and deliberately opts in.
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:ReverseProxy";

    /// <summary>
    ///     This constant contains the default header name a caller's own shared secret is
    ///     expected in, used whenever <see cref="SharedSecretHeaderName" /> is left blank.
    /// </summary>
    public const string DefaultSharedSecretHeaderName = "X-Ninlil-Proxy-Secret";

    /// <summary>
    ///     This property controls whether forwarded headers are honored at all. When
    ///     <see langword="false" />, every request is answered using the raw connection's own
    ///     peer address exactly as it always has been -- no middleware from this feature is even
    ///     added to the pipeline.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains every individual IP address (no CIDR notation) allowed to
    ///     claim a forwarded client address on a request's behalf -- the exact single-address
    ///     entries every real reverse proxy this project's own homelab/small-org target scale is
    ///     likely to run (a single, fixed-address Nginx/Caddy/HAProxy instance) needs. Combined
    ///     with <see cref="KnownNetworks" />; a request whose own connecting peer address matches
    ///     neither this list nor any network in <see cref="KnownNetworks" /> is never trusted,
    ///     regardless of <see cref="Enable" />.
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>
    ///     This property contains every CIDR network (for example <c>10.0.0.0/24</c>) allowed to
    ///     claim a forwarded client address on a request's behalf -- for a reverse proxy whose
    ///     own address is not fixed (a container orchestrator's own internal network, for
    ///     example). Parsed the same way <c>ClientGroupNetworksController</c> already parses a
    ///     client group's own network entries, via <see cref="System.Net.IPNetwork" />'s own
    ///     parser.
    /// </summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>
    ///     This property contains the request header name a shared secret is read from, when
    ///     <see cref="SharedSecret" /> is configured. Defaults to
    ///     <see cref="DefaultSharedSecretHeaderName" />.
    /// </summary>
    public string SharedSecretHeaderName { get; set; } = DefaultSharedSecretHeaderName;

    /// <summary>
    ///     This property contains an optional shared secret this host requires
    ///     <see cref="SharedSecretHeaderName" /> to carry, on top of the
    ///     <see cref="KnownProxies" />/<see cref="KnownNetworks" /> allowlist, before a request's
    ///     forwarded headers are honored. Real defense-in-depth on top of the allowlist alone: it
    ///     protects against a request that reaches this host directly from an address that
    ///     happens to sit inside an allowlisted CIDR block (a shared or dynamically-assigned
    ///     address, a misconfigured internal network) without actually having passed through the
    ///     real, correctly-configured reverse proxy, which is expected to be the only thing that
    ///     ever attaches this header. <see langword="null" />/empty means the allowlist alone is
    ///     the trust boundary -- no header is required at all.
    /// </summary>
    public string SharedSecret { get; set; }
}
