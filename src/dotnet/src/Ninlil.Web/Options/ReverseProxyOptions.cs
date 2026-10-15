// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:ReverseProxy</c> configuration section: whether the
///     shared Kestrel listener (the REST API, the admin console, the SignalR hub and
///     DNS-over-HTTPS) trusts a reverse proxy's <c>X-Forwarded-For</c> and
///     <c>X-Forwarded-Proto</c> headers instead of the TCP peer address. It is off by default,
///     like other settings that widen what the server accepts: honoring forwarded headers lets a
///     client that connects directly claim to be someone else, so it should be on only when a
///     reverse proxy sits in front. <c>Program.cs</c> reads it at startup to build the
///     forwarded-headers middleware, so a change needs a restart.
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:ReverseProxy";

    /// <summary>
    ///     This constant contains the header name a shared secret is read from when
    ///     <see cref="SharedSecretHeaderName" /> is blank.
    /// </summary>
    public const string DefaultSharedSecretHeaderName = "X-Ninlil-Proxy-Secret";

    /// <summary>
    ///     This constant contains the Data Protection purpose string <see cref="SharedSecretEncrypted" />
    ///     is protected with. A purpose of its own keeps a payload protected for another secret
    ///     from decrypting as this one, so a value copied between two settings fails closed.
    /// </summary>
    public const string SharedSecretProtectorPurpose = "Ninlil.Web.ReverseProxy.SharedSecret";

    /// <summary>
    ///     This property controls whether forwarded headers are honored. When
    ///     <see langword="false" /> (the default), requests use the TCP peer address and the
    ///     forwarded-headers middleware is not added to the pipeline.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the individual IP addresses (no CIDR notation) allowed to supply
    ///     a forwarded client address, which covers a single fixed-address Nginx, Caddy or HAProxy
    ///     in front of the server. A request from a peer in neither this list nor
    ///     <see cref="KnownNetworks" /> is never trusted.
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>
    ///     This property contains the CIDR networks (for example <c>10.0.0.0/24</c>) allowed to
    ///     supply a forwarded client address, for a reverse proxy without a fixed address, such as
    ///     one on a container network. Entries are parsed with <see cref="System.Net.IPNetwork" />.
    /// </summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>
    ///     This property contains the request header a shared secret is read from when
    ///     <see cref="SharedSecret" /> is set. Defaults to <see cref="DefaultSharedSecretHeaderName" />.
    /// </summary>
    public string SharedSecretHeaderName { get; set; } = DefaultSharedSecretHeaderName;

    /// <summary>
    ///     This property contains an optional shared secret that
    ///     <see cref="SharedSecretHeaderName" /> must carry, in addition to the allowlist, before a
    ///     request's forwarded headers are honored. It guards against a request that comes
    ///     straight from an address inside an allowlisted network (a shared or dynamic address, a
    ///     misconfigured internal network) without passing through the proxy, which should be the
    ///     only thing that adds the header. <see langword="null" /> or empty means the allowlist
    ///     alone decides.
    ///     <para>
    ///         The file stores the secret in <see cref="SharedSecretEncrypted" />, not here, so the
    ///         secret is not readable from the file or from a backup of it. A non-empty value in
    ///         this property from the configuration (for example the
    ///         <c>NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET</c> variable) wins over
    ///         <see cref="SharedSecretEncrypted" />. At startup
    ///         <c>ConfigurationSecretResolution</c> replaces the value of this property with the
    ///         decrypted secret, so the middleware reads the plaintext here. When the stored secret
    ///         cannot be decrypted, it is set to a random value nobody can present, so no proxy is
    ///         trusted (fail closed).
    ///     </para>
    /// </summary>
    public string SharedSecret { get; set; }

    /// <summary>
    ///     This property contains the shared secret as a Data Protection payload that only this
    ///     node's key ring can read. <c>ReverseProxySettingsController</c> and cluster sync write
    ///     it (each node encrypts the secret with its own key ring), and
    ///     <c>ConfigurationSecretMigrationService</c> writes it for a plaintext secret an earlier
    ///     version left in the file. It is not set by hand: a payload from another node's key ring
    ///     cannot be decrypted. A non-empty <see cref="SharedSecret" /> from the configuration
    ///     wins over it.
    /// </summary>
    public string SharedSecretEncrypted { get; set; } = "";
}
