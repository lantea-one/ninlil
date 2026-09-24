using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of a TLS-based transport's own server certificate
///     configuration (<see cref="TlsCertificateOptions" />) -- shared by DNS-over-TLS and
///     DNS-over-QUIC, the two transports that actually consult it inside this host's own combined
///     process (DNS-over-HTTPS, mapped onto this host's shared Kestrel instance rather than
///     self-hosting its own, uses that shared listener's own certificate instead -- see
///     <see cref="DnsOverHttpsSettingsDetail" />'s own doc comment for why no equivalent field
///     exists there). Reflects the exact same "operator-provided PFX path, or a self-managed
///     self-signed certificate" shape <see cref="TlsCertificateOptions" /> itself already uses --
///     this record does not introduce a raw-key-material upload flow, deliberately.
/// </summary>
/// <param name="PfxPath">
///     The path to an operator-provided PKCS#12 certificate file, including its private key. Empty
///     means "generate and manage a self-signed certificate instead."
/// </param>
/// <param name="PfxPasswordConfigured">
///     Whether <see cref="TlsCertificateOptions.PfxPassword" /> currently has a value -- the
///     password itself is never returned by a <c>GET</c>, the same reveal-once posture every other
///     secret-bearing setting in this project already follows.
/// </param>
/// <param name="SelfSignedSubjectAlternativeNames">
///     The subject alternative names a self-signed certificate (generated only when
///     <see cref="PfxPath" /> is empty) is issued for.
/// </param>
/// <param name="CertificateReloadPollInterval">
///     How often this transport's own <see cref="ReloadableTlsCertificateProvider" /> re-checks
///     whether the certificate this configuration points at has been replaced on disk.
/// </param>
/// <param name="AllowedProtocols">
///     Which TLS protocol versions this transport's own handshake will accept, by name (for
///     example <c>["Tls12", "Tls13"]</c>). An empty list means <see cref="System.Security.Authentication.SslProtocols.None" />
///     -- let the OS/runtime negotiate its own best-available default, per
///     <see cref="TlsCertificateOptions.AllowedProtocols" />'s own doc comment.
/// </param>
public record TlsCertificateSettingsDetail(
    string PfxPath,
    bool PfxPasswordConfigured,
    List<string> SelfSignedSubjectAlternativeNames,
    TimeSpan CertificateReloadPollInterval,
    List<string> AllowedProtocols
);

/// <summary>
///     This record is the REST representation of Plain DNS's own <c>PlainProtocolOptions</c>.
///     Deliberately has no <c>Enable</c> field -- Plain DNS has none of its own either, since it is
///     this project's baseline "this is a DNS server at all" transport rather than a
///     capability-expanding toggle, and starts unconditionally.
/// </summary>
/// <param name="Port">The port both the UDP and TCP listeners bind to.</param>
/// <param name="ListenAddresses">
///     The local IP addresses both the UDP and TCP listeners binds to, one socket per address. An empty list means every
///     address (one dual-stack socket), the default. Restart-required, like the port.
/// </param>
/// <param name="TcpIdleTimeout">How long a TCP connection may sit completely idle before this server closes it.</param>
/// <param name="MaxConcurrentTcpConnections">How many TCP connections this server accepts at once.</param>
public record PlainTransportSettingsDetail(
    int Port,
    List<string> ListenAddresses,
    TimeSpan TcpIdleTimeout,
    int MaxConcurrentTcpConnections
);

/// <summary>
///     This record is the REST representation of DNS-over-TLS's own <c>DnsOverTlsOptions</c>.
///     Deliberately excludes <c>ProxyProtocol</c> -- already exposed via
///     <see cref="Controllers.ReverseProxySettingsController" />'s own <c>dnsOverTls</c> nested
///     field, alongside the HTTP-side reverse-proxy trust settings it is conceptually paired with;
///     duplicating it here would give the same configuration section two different REST addresses.
/// </summary>
/// <param name="Enable">
///     Whether this transport listens at all. <b>Genuinely live</b>: toggling this field, via
///     <see cref="Controllers.TransportSettingsController.PatchDnsOverTls" />, starts or stops the
///     real, already-running <c>dns-over-tls</c> listener registered with
///     <see cref="Services.IManagedServiceRegistry" />, with no restart.
/// </param>
/// <param name="Port">The port this transport binds to. Restart-required -- rebinding a socket to a new port needs this specific listener to restart.</param>
/// <param name="ListenAddresses">
///     The local IP addresses this transport binds to, one socket per address. An empty list means every
///     address (one dual-stack socket), the default. Restart-required, like the port.
/// </param>
/// <param name="IdleTimeout">How long a TLS connection may sit completely idle before this server closes it. Restart-required -- read once when a connection is accepted, under this listener's own currently-bound socket.</param>
/// <param name="MaxConcurrentConnections">How many TLS connections this server accepts at once. Restart-required, for the same reason as <see cref="IdleTimeout" />.</param>
/// <param name="Certificate">This transport's own server certificate configuration. Restart-required.</param>
public record DnsOverTlsSettingsDetail(
    bool Enable,
    int Port,
    List<string> ListenAddresses,
    TimeSpan IdleTimeout,
    int MaxConcurrentConnections,
    TlsCertificateSettingsDetail Certificate
);

/// <summary>
///     This record is the REST representation of DNS-over-HTTPS's own <c>DnsOverHttpsOptions</c>,
///     as actually consulted by this combined host: <c>Port</c> and <c>Certificate</c> are
///     deliberately excluded, because <see cref="Ninlil.Dns.Protocol.DnsOverHttps.Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />
///     maps RFC 8484's GET/POST forms directly onto this host's own already-running shared Kestrel
///     instance -- both fields are specific to <c>DnsOverHttpsHostedService</c>'s own separate,
///     self-hosted Kestrel instance, which this composition root never starts, and would be
///     silently ignored (misleadingly, if exposed here as if they mattered) if written through this
///     controller. This host's own shared HTTP(S) port and certificate decide both instead -- and
///     for the same reason there is no <c>ListenAddresses</c> here: DNS-over-HTTPS is answered on
///     whatever addresses the web listener (<c>DnsServer:Web</c>) binds.
/// </summary>
/// <param name="Enable">
///     Whether a DNS-over-HTTPS request reaching the already-mapped route is answered. <b>Genuinely
///     live</b>: toggling this field starts or stops the real <c>dns-over-https</c> transport gate
///     registered with <see cref="Services.IManagedServiceRegistry" />, with no restart -- the
///     route itself is always mapped, regardless of this flag's own value at process startup, so
///     there is no "route was never mapped" edge case left to work around (see that extension
///     method's own doc comment).
/// </param>
/// <param name="Path">
///     The HTTP path both the GET and POST forms of a DoH query are answered at. Needs a full
///     <b>process</b> restart, not merely a listener restart -- ASP.NET Core's Minimal API routing
///     has no supported "remap this route" operation once the host has started.
/// </param>
/// <param name="MaxMessageSizeBytes">
///     How large a single DNS message this transport accepts may be. Needs a full process restart
///     for the same reason as <see cref="Path" /> -- both are captured once, into the closure the
///     route itself was mapped with.
/// </param>
public record DnsOverHttpsSettingsDetail(bool Enable, string Path, int MaxMessageSizeBytes);

/// <summary>
///     This record is the REST representation of DNS-over-QUIC's own <c>DnsOverQuicOptions</c>.
/// </summary>
/// <param name="Enable">
///     Whether this transport listens at all. <b>Genuinely live</b>: toggling this field starts or
///     stops the real, already-running <c>dns-over-quic</c> listener registered with
///     <see cref="Services.IManagedServiceRegistry" />, with no restart.
/// </param>
/// <param name="Port">The port this transport binds to. Restart-required.</param>
/// <param name="ListenAddresses">
///     The local IP addresses this transport binds to, one socket per address. An empty list means every
///     address (one dual-stack socket), the default. Restart-required, like the port. QUIC answers only on these
///     addresses, but its library still reserves the UDP port on every address, and <c>::</c> also
///     answers IPv4 -- see <c>DnsOverQuicOptions.ListenAddresses</c>.
/// </param>
/// <param name="IdleTimeout">How long a QUIC connection may sit completely idle before this server closes it. Restart-required.</param>
/// <param name="MaxConcurrentBidirectionalStreams">How many bidirectional streams a single QUIC connection may have open at once. Restart-required.</param>
/// <param name="Certificate">This transport's own server certificate configuration. Restart-required.</param>
public record DnsOverQuicSettingsDetail(
    bool Enable,
    int Port,
    List<string> ListenAddresses,
    TimeSpan IdleTimeout,
    int MaxConcurrentBidirectionalStreams,
    TlsCertificateSettingsDetail Certificate
);

/// <summary>
///     This record is the REST representation of DNSCrypt's own <c>DnsCryptOptions</c>.
/// </summary>
/// <param name="Enable">
///     Whether this transport listens at all. <b>Genuinely live</b>: toggling this field starts or
///     stops the real, already-running <c>dnscrypt-udp</c>/<c>dnscrypt-tcp</c> listeners <em>and</em>
///     <c>dnscrypt-certificate-rotation</c> (the background loop that actually issues this
///     resolver's own DNSCrypt certificates -- with no certificate, a live-started listener could
///     accept connections but never answer a real encrypted query), all three registered with
///     <see cref="Services.IManagedServiceRegistry" />, with no restart.
/// </param>
/// <param name="Port">The port this transport binds to, for both UDP and TCP. Restart-required.</param>
/// <param name="ListenAddresses">
///     The local IP addresses this transport, for both UDP and TCP, binds to, one socket per address. An empty list means every
///     address (one dual-stack socket), the default. Restart-required, like the port.
/// </param>
/// <param name="ProviderName">The zone this resolver advertises its DNSCrypt certificate discovery TXT record under. Restart-required -- read once, at this transport's own startup.</param>
/// <param name="TcpIdleTimeout">How long a TCP connection may sit completely idle before this server closes it. Restart-required.</param>
/// <param name="MaxConcurrentTcpConnections">How many TCP connections this server accepts at once. Restart-required.</param>
/// <param name="EnabledEsVersions">
///     Every DNSCrypt cipher this resolver keeps a continuously valid certificate for, by name (for
///     example <c>["XChaCha20Poly1305", "XSalsa20Poly1305"]</c>). Restart-required --
///     <c>DnsCryptCertificateRotationService</c> reads this once per rotation check, not per
///     request, so a change here needs that background loop (or the whole transport, since this
///     controller restarts it together with the listeners) to restart before it takes effect.
/// </param>
/// <param name="CertificateRotationInterval">How often a fresh certificate is considered for every enabled cipher. Restart-required.</param>
/// <param name="CertificateValidityDuration">How long a newly generated certificate stays valid for. Restart-required.</param>
/// <param name="CertificateClockSkewTolerance">How far in the past a newly generated certificate's own validity start is backdated. Restart-required.</param>
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
