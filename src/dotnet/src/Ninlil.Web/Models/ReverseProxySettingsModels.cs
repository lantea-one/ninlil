// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the reverse proxy trust configuration that
///     <c>GET /api/reverse-proxy-settings</c> returns. The shared secret is never returned;
///     <see cref="SharedSecretConfigured" /> says whether one is set, as
///     <c>TwoFactorStatus.HasAuthenticator</c> does for an authenticator. A <c>PATCH</c> accepts
///     these fields with <c>sharedSecret</c> in place of <see cref="SharedSecretConfigured" />;
///     the last two fields are read-only.
/// </summary>
/// <param name="Enable">Whether forwarded headers are honored at all.</param>
/// <param name="KnownProxies">The individual IP addresses allowed to supply a forwarded client address.</param>
/// <param name="KnownNetworks">The CIDR networks allowed to supply a forwarded client address.</param>
/// <param name="SharedSecretHeaderName">The request header a shared secret is read from, when one is set.</param>
/// <param name="SharedSecretConfigured">Whether a shared secret is set.</param>
/// <param name="DnsOverTls">The PROXY protocol trust settings for the DNS-over-TLS listener.</param>
/// <param name="LastModifiedUtc">
///     When the settings were last written through the REST API, used by cluster config sync, or
///     <see langword="null" /> if they never have been.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node the settings were last written on, or <see langword="null" /> together
///     with <see cref="LastModifiedUtc" />.
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
///     This record is the REST shape of <c>Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol.ProxyProtocolOptions</c>:
///     whether <c>DnsOverTlsListener</c> accepts an HAProxy PROXY protocol header (v1 text or v2
///     binary) before the TLS handshake, and which addresses and networks may send one. Unlike the
///     HTTP settings above it has no shared secret; <c>ProxyProtocolOptions</c> explains why.
/// </summary>
/// <param name="Enable">Whether a connection from a trusted source is checked for a PROXY protocol header.</param>
/// <param name="KnownProxies">The individual IP addresses allowed to send a PROXY protocol header.</param>
/// <param name="KnownNetworks">The CIDR networks allowed to send a PROXY protocol header.</param>
public record DnsOverTlsProxyProtocolSettingsDetail(
    bool Enable,
    IReadOnlyList<string> KnownProxies,
    IReadOnlyList<string> KnownNetworks
);
