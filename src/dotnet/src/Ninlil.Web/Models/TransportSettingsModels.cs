using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the server certificate configuration of a TLS-based transport
///     (<see cref="TlsCertificateOptions" />), used by DNS-over-TLS and DNS-over-QUIC.
///     DNS-over-HTTPS runs on the shared Kestrel listener and uses its certificate, so it has no
///     such field (see <see cref="DnsOverHttpsSettingsDetail" />). The shape is the one
///     <see cref="TlsCertificateOptions" /> uses: a PFX file path, or a self-signed certificate the
///     server manages. Key material cannot be uploaded through the API.
/// </summary>
/// <param name="PfxPath">
///     The path to an operator-provided PKCS#12 file with the certificate and private key. Empty
///     means the server generates and manages a self-signed certificate.
/// </param>
/// <param name="PfxPasswordConfigured">
///     Whether <see cref="TlsCertificateOptions.PfxPassword" /> has a value. The password itself is
///     never returned, as with every other secret.
/// </param>
/// <param name="SelfSignedSubjectAlternativeNames">
///     The subject alternative names of the self-signed certificate, which is generated only when
///     <see cref="PfxPath" /> is empty.
/// </param>
/// <param name="CertificateReloadPollInterval">
///     How often the transport's <see cref="ReloadableTlsCertificateProvider" /> checks whether the
///     certificate file has been replaced on disk.
/// </param>
/// <param name="AllowedProtocols">
///     The TLS protocol versions the handshake accepts, by name (for example
///     <c>["Tls12", "Tls13"]</c>). An empty list means
///     <see cref="System.Security.Authentication.SslProtocols.None" />: the operating system picks
///     its default (see <see cref="TlsCertificateOptions.AllowedProtocols" />).
/// </param>
public record TlsCertificateSettingsDetail(
    string PfxPath,
    bool PfxPasswordConfigured,
    List<string> SelfSignedSubjectAlternativeNames,
    TimeSpan CertificateReloadPollInterval,
    List<string> AllowedProtocols
);

/// <summary>
///     This record is the plain DNS transport settings (<c>PlainProtocolOptions</c>) that
///     <c>GET /api/transport-settings/plain</c> returns. It has no <c>Enable</c> field because
///     plain DNS has none: it is the baseline transport and always starts. Every field needs a
///     listener restart.
/// </summary>
/// <param name="Port">The port the UDP and TCP listeners bind to.</param>
/// <param name="ListenAddresses">
///     The local IP addresses the UDP and TCP listeners bind to, one socket per address. An empty
///     list (the default) means every address, through one dual-stack socket.
/// </param>
/// <param name="TcpIdleTimeout">How long a TCP connection may stay idle before the server closes it.</param>
/// <param name="MaxConcurrentTcpConnections">How many TCP connections the server accepts at once.</param>
public record PlainTransportSettingsDetail(
    int Port,
    List<string> ListenAddresses,
    TimeSpan TcpIdleTimeout,
    int MaxConcurrentTcpConnections
);

/// <summary>
///     This record is the DNS-over-TLS settings (<c>DnsOverTlsOptions</c>) that
///     <c>GET /api/transport-settings/dns-over-tls</c> returns. <c>ProxyProtocol</c> is left out:
///     it is exposed once, as the <c>dnsOverTls</c> field of
///     <see cref="Controllers.ReverseProxySettingsController" />, next to the HTTP reverse proxy
///     trust settings it belongs with.
/// </summary>
/// <param name="Enable">
///     Whether the transport listens. A change applies at once:
///     <see cref="Controllers.TransportSettingsController.PatchDnsOverTls" /> starts or stops the
///     <c>dns-over-tls</c> listener through <see cref="Services.IManagedServiceRegistry" />.
/// </param>
/// <param name="Port">The port the transport binds to. A change needs a listener restart.</param>
/// <param name="ListenAddresses">
///     The local IP addresses the transport binds to, one socket per address. An empty list (the
///     default) means every address, through one dual-stack socket. A change needs a listener
///     restart.
/// </param>
/// <param name="IdleTimeout">How long a TLS connection may stay idle before the server closes it. A change needs a listener restart.</param>
/// <param name="MaxConcurrentConnections">How many TLS connections the server accepts at once. A change needs a listener restart.</param>
/// <param name="Certificate">The transport's server certificate configuration. A change needs a listener restart.</param>
public record DnsOverTlsSettingsDetail(
    bool Enable,
    int Port,
    List<string> ListenAddresses,
    TimeSpan IdleTimeout,
    int MaxConcurrentConnections,
    TlsCertificateSettingsDetail Certificate
);

/// <summary>
///     This record is the DNS-over-HTTPS settings (<c>DnsOverHttpsOptions</c>) that
///     <c>GET /api/transport-settings/dns-over-https</c> returns, limited to the fields the server
///     uses. <see cref="Ninlil.Dns.Protocol.DnsOverHttps.Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />
///     maps the RFC 8484 GET and POST forms onto the shared Kestrel listener, so the shared web
///     listener's port, certificate and addresses (<c>DnsServer:Web</c>) apply. <c>Port</c> and
///     <c>Certificate</c> belong to the self-hosted <c>DnsOverHttpsHostedService</c>, which this
///     server never starts, so they are left out rather than shown as if they had an effect; for
///     the same reason there is no <c>ListenAddresses</c>.
/// </summary>
/// <param name="Enable">
///     Whether a request on the DNS-over-HTTPS route is answered. A change applies at once: it
///     opens or closes the <c>dns-over-https</c> transport gate through
///     <see cref="Services.IManagedServiceRegistry" />. The route is mapped whatever this flag was
///     at startup (see the extension method above).
/// </param>
/// <param name="Path">
///     The HTTP path the GET and POST forms of a DoH query are answered at. A change needs a
///     process restart, not just a listener restart: routes cannot be remapped once the host has
///     started.
/// </param>
/// <param name="MaxMessageSizeBytes">
///     The largest DNS message the transport accepts. Like <see cref="Path" />, it is captured
///     when the route is mapped at startup, so a change needs a process restart.
/// </param>
public record DnsOverHttpsSettingsDetail(bool Enable, string Path, int MaxMessageSizeBytes);

/// <summary>
///     This record is the DNS-over-QUIC settings (<c>DnsOverQuicOptions</c>) that
///     <c>GET /api/transport-settings/dns-over-quic</c> returns.
/// </summary>
/// <param name="Enable">
///     Whether the transport listens. A change applies at once: it starts or stops the
///     <c>dns-over-quic</c> listener through <see cref="Services.IManagedServiceRegistry" />.
/// </param>
/// <param name="Port">The port the transport binds to. A change needs a listener restart.</param>
/// <param name="ListenAddresses">
///     The local IP addresses the transport binds to, one socket per address. An empty list (the
///     default) means every address, through one dual-stack socket. A change needs a listener
///     restart. QUIC answers only on these addresses, but its library still reserves the UDP port
///     on every address, and <c>::</c> also answers IPv4 (see
///     <c>DnsOverQuicOptions.ListenAddresses</c>).
/// </param>
/// <param name="IdleTimeout">How long a QUIC connection may stay idle before the server closes it. A change needs a listener restart.</param>
/// <param name="MaxConcurrentBidirectionalStreams">How many bidirectional streams one QUIC connection may have open at once. A change needs a listener restart.</param>
/// <param name="Certificate">The transport's server certificate configuration. A change needs a listener restart.</param>
public record DnsOverQuicSettingsDetail(
    bool Enable,
    int Port,
    List<string> ListenAddresses,
    TimeSpan IdleTimeout,
    int MaxConcurrentBidirectionalStreams,
    TlsCertificateSettingsDetail Certificate
);

/// <summary>
///     This record is the DNSCrypt settings (<c>DnsCryptOptions</c>) that
///     <c>GET /api/transport-settings/dnscrypt</c> returns.
/// </summary>
/// <param name="Enable">
///     Whether the transport listens. A change applies at once: it starts or stops the
///     <c>dnscrypt-udp</c> and <c>dnscrypt-tcp</c> listeners and the
///     <c>dnscrypt-certificate-rotation</c> loop through
///     <see cref="Services.IManagedServiceRegistry" />. The loop issues the resolver's
///     certificates; without one, a listener could accept connections but not answer an
///     encrypted query.
/// </param>
/// <param name="Port">The port the transport binds to, for UDP and TCP. A change needs a listener restart.</param>
/// <param name="ListenAddresses">
///     The local IP addresses the transport binds to, for UDP and TCP, one socket per address. An
///     empty list (the default) means every address, through one dual-stack socket. A change
///     needs a listener restart.
/// </param>
/// <param name="ProviderName">The provider name certificates are published under (clients query the TXT record <c>2.dnscrypt-cert.&lt;ProviderName&gt;</c>). Empty by default, and the listeners refuse to start while it is empty. A change needs a listener restart.</param>
/// <param name="TcpIdleTimeout">How long a TCP connection may stay idle before the server closes it. A change needs a listener restart.</param>
/// <param name="MaxConcurrentTcpConnections">How many TCP connections the server accepts at once. A change needs a listener restart.</param>
/// <param name="EnabledEsVersions">
///     The DNSCrypt ciphers the resolver keeps a valid certificate for, by name (for example
///     <c>["XChaCha20Poly1305", "XSalsa20Poly1305"]</c>). <c>DnsCryptCertificateRotationService</c>
///     reads it when it starts, so a change applies once that loop restarts, for example by
///     turning the transport off and on.
/// </param>
/// <param name="CertificateRotationInterval">How often a new certificate is considered for each enabled cipher. A change needs a restart of the rotation loop.</param>
/// <param name="CertificateValidityDuration">How long a new certificate stays valid. A change needs a restart of the rotation loop.</param>
/// <param name="CertificateClockSkewTolerance">How far a new certificate's validity start is backdated, to allow for client clock skew. A change needs a restart of the rotation loop.</param>
public record DnsCryptSettingsDetail(
    bool Enable,
    int Port,
    List<string> ListenAddresses,
    string ProviderName,
    TimeSpan TcpIdleTimeout,
    int MaxConcurrentTcpConnections,
    List<string> EnabledEsVersions,
    TimeSpan CertificateRotationInterval,
    TimeSpan CertificateValidityDuration,
    TimeSpan CertificateClockSkewTolerance
);
