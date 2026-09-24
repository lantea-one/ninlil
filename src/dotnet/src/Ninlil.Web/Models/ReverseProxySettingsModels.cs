// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     reverse-proxy trust configuration. <see cref="SharedSecretConfigured" /> reports whether a
///     shared secret is set without ever revealing its value -- the same "configured/not
///     configured" indicator <c>TwoFactorStatus.HasAuthenticator</c> and a webhook subscription's
///     own read-only HMAC-signing-secret indicator already use for a secret this endpoint's own
///     <c>Get</c> action never returns.
/// </summary>
/// <param name="Enable">Whether forwarded headers are currently honored at all.</param>
/// <param name="KnownProxies">Every individual IP address allowed to claim a forwarded client address.</param>
/// <param name="KnownNetworks">Every CIDR network allowed to claim a forwarded client address.</param>
/// <param name="SharedSecretHeaderName">The request header a shared secret is read from, when one is configured.</param>
/// <param name="SharedSecretConfigured">Whether a shared secret is currently configured.</param>
/// <param name="DnsOverTls">This host's own currently-effective DNS-over-TLS PROXY protocol trust configuration.</param>
/// <param name="LastModifiedUtc">
///     When this settings section was last written (the cluster's own operational config sync), or
///     <see langword="null" /> if it has never been written through this REST surface.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node this settings section was last written on, or <see langword="null" />
///     alongside <see cref="LastModifiedUtc" />.
/// </param>
public record ReverseProxySettingsDetail(
    bool Enable,
    IReadOnlyList<string> KnownProxies,
    IReadOnlyList<string> KnownNetworks,
    string SharedSecretHeaderName,
    bool SharedSecretConfigured,
    DnsOverTlsProxyProtocolSettingsDetail DnsOverTls,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);

/// <summary>
///     This record is the REST representation of <c>Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol.ProxyProtocolOptions</c>
///     -- whether <c>DnsOverTlsListener</c> accepts a HAProxy-style PROXY protocol header (v1
///     text or v2 binary) ahead of its own TLS handshake, and which addresses/networks are
///     trusted to send one. Unlike the HTTP-side allowlist above, there is no shared-secret field
///     here at all -- see <c>ProxyProtocolOptions</c>'s own doc comment for why.
/// </summary>
/// <param name="Enable">Whether a connection from a recognized source is inspected for a PROXY protocol header at all.</param>
/// <param name="KnownProxies">Every individual IP address allowed to open a connection carrying a PROXY protocol header.</param>
/// <param name="KnownNetworks">Every CIDR network allowed to open a connection carrying a PROXY protocol header.</param>
public record DnsOverTlsProxyProtocolSettingsDetail(
    bool Enable,
    IReadOnlyList<string> KnownProxies,
    IReadOnlyList<string> KnownNetworks
);
