// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsCrypt</c> section of the application's
///     configuration and controls how <see cref="DnsCryptCertificateRotationService" /> (and,
///     in <c>Ninlil.Dns.Protocol.DnsCrypt</c>, the actual listener) behave.
/// </summary>
public class DnsCryptOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsCrypt";

    /// <summary>
    ///     This property controls whether this transport listens at all. Off by default, the
    ///     same reasoning as every other transport's own <c>Enable</c> toggle -- a new
    ///     listening port is a real, visible configuration change an operator should opt into.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to, for both UDP and TCP.
    ///     Deliberately not 443 -- this project's DoH transport already claims TCP:443 in the
    ///     same process, so DNSCrypt's usual "blend in on 443" positioning is only partially
    ///     achievable here regardless; a clean, unambiguous distinct port was chosen instead.
    /// </summary>
    public int Port { get; set; } = 5353;

    /// <summary>
    ///     This property contains the local addresses this transport, for both UDP and TCP, binds to, comma-separated -- for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNSCrypt off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the zone this resolver advertises its DNSCrypt certificate
    ///     discovery TXT record under -- clients query
    ///     <c>2.dnscrypt-cert.&lt;ProviderName&gt;</c> (protocol major version <c>2</c>, fixed)
    ///     to learn this resolver's current certificate(s). Left blank by default; an operator
    ///     enabling this transport must set this to a real zone they control.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long a TCP connection may sit completely idle -- no
    ///     complete query received -- before this server closes it, the same reasoning RFC 7766
    ///     §6.2.3 gives for plain DNS-over-TCP.
    /// </summary>
    public TimeSpan TcpIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TCP connections this server accepts at once.
    /// </summary>
    public int MaxConcurrentTcpConnections { get; set; } = 512;

    /// <summary>
    ///     This property contains every <see cref="DnsCryptEsVersion" /> this resolver keeps a
    ///     continuously valid certificate for -- both currently, matching the explicit decision
    ///     to support both the current spec's required cipher and the legacy one many
    ///     real-world clients still use.
    /// </summary>
    public List<DnsCryptEsVersion> EnabledEsVersions { get; set; } =
    [DnsCryptEsVersion.XChaCha20Poly1305, DnsCryptEsVersion.XSalsa20Poly1305];

    /// <summary>
    ///     This property contains how often <see cref="DnsCryptCertificateRotationService" />
    ///     checks whether a fresh certificate is needed for any enabled cipher -- both to
    ///     replace one nearing expiry and to promptly notice (and immediately correct) any gap
    ///     in coverage.
    /// </summary>
    public TimeSpan CertificateRotationInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property contains how long a newly generated certificate stays valid for --
    ///     deliberately longer than <see cref="CertificateRotationInterval" />, so there is
    ///     always at least one, and usually two, overlapping valid certificates per cipher at
    ///     any moment: an in-flight client that learned the previous certificate is never left
    ///     without a still-valid one to use.
    /// </summary>
    public TimeSpan CertificateValidityDuration { get; set; } = TimeSpan.FromHours(48);

    /// <summary>
    ///     This property contains how far in the past a newly generated certificate's own
    ///     <c>&lt;ts-start&gt;</c> is backdated, to tolerate ordinary clock skew between this
    ///     resolver and a client that fetched the certificate just before it would otherwise
    ///     have become valid.
    /// </summary>
    public TimeSpan CertificateClockSkewTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains where this resolver's own long-term Ed25519 provider key
    ///     pair and every currently tracked certificate (including each one's own resolver
    ///     secret key) are persisted, so a restart does not force every client to re-discover a
    ///     brand new provider identity. Not encrypted at rest via <c>IDataProtector</c> --
    ///     deliberately consistent with how <c>Server.Core</c>'s self-signed TLS certificate is
    ///     handled (an automatically generated, operator-invisible artifact with no external
    ///     trust chain depending on it, not an operator-managed secret like a TSIG key or a
    ///     DNSSEC signing key), protected by filesystem permissions only.
    /// </summary>
    public string CertificateStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "dnscrypt-certificates.json");
}
