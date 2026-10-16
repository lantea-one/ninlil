// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsCrypt</c> section of the application's
///     configuration and holds the settings of <see cref="DnsCryptCertificateRotationService" />
///     and of the DNSCrypt listeners in <c>Ninlil.Dns.Protocol.DnsCrypt</c>. The transport
///     settings controller in <c>Ninlil.Web</c> edits them at runtime.
/// </summary>
public class DnsCryptOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsCrypt";

    /// <summary>
    ///     This property controls whether this transport listens. It is off by default, like the
    ///     other optional transports, because opening a new port should be the operator's choice.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to, for both UDP and TCP. It is
    ///     not 443, which DNSCrypt servers often use to blend in, because the DNS-over-HTTPS
    ///     transport uses TCP 443 by default in the same process, and not 5353, which multicast
    ///     DNS uses (RFC 6762) and which would clash with an mDNS responder on the same host.
    /// </summary>
    public int Port { get; set; } = 5443;

    /// <summary>
    ///     This property contains the local addresses this transport binds to for both UDP and
    ///     TCP, comma-separated, for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNSCrypt off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the provider name under which the certificates are published:
    ///     clients query the TXT record <c>2.dnscrypt-cert.&lt;ProviderName&gt;</c> (protocol
    ///     major version <c>2</c>) to learn the current certificates. The name works with or
    ///     without the <c>2.dnscrypt-cert.</c> prefix (matched case-insensitively, never doubled):
    ///     <c>resolver.example</c> and <c>2.dnscrypt-cert.resolver.example</c> publish at the same
    ///     name. It is empty by default, and the listeners refuse to start while it is empty or
    ///     not a valid domain name.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long a TCP connection may stay idle, with no complete query
    ///     received, before the server closes it, for the reasons RFC 7766 §6.2.3 gives for plain
    ///     DNS over TCP.
    /// </summary>
    public TimeSpan TcpIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TCP connections this server accepts at once.
    /// </summary>
    public int MaxConcurrentTcpConnections { get; set; } = 512;

    /// <summary>
    ///     This property contains every <see cref="DnsCryptEsVersion" /> the resolver keeps a valid
    ///     certificate for. The default is both: the cipher the current spec requires and the
    ///     older one many clients still use.
    /// </summary>
    public List<DnsCryptEsVersion> EnabledEsVersions { get; set; } =
    [DnsCryptEsVersion.XChaCha20Poly1305, DnsCryptEsVersion.XSalsa20Poly1305];

    /// <summary>
    ///     This property contains how often <see cref="DnsCryptCertificateRotationService" />
    ///     checks whether a new certificate is needed for an enabled cipher, either to replace
    ///     one that expires before the next check or to fill a gap.
    /// </summary>
    public TimeSpan CertificateRotationInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property contains how long a new certificate stays valid. It should be longer than
    ///     <see cref="CertificateRotationInterval" />: with the defaults a new certificate is made
    ///     about a day before the previous one expires, so there are usually two valid
    ///     certificates per cipher and a client that learned the older one can keep using it.
    /// </summary>
    public TimeSpan CertificateValidityDuration { get; set; } = TimeSpan.FromHours(48);

    /// <summary>
    ///     This property contains how far a new certificate's <c>&lt;ts-start&gt;</c> is
    ///     backdated, so a client whose clock is slightly behind the resolver's still accepts
    ///     it.
    /// </summary>
    public TimeSpan CertificateClockSkewTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains the file where <see cref="FileDnsCryptCertificateStore" /> keeps
    ///     the long-term Ed25519 provider key pair and every tracked certificate with its resolver
    ///     secret key, so a restart keeps the same provider identity (the provider public key is
    ///     what clients' <c>sdns://</c> stamps pin). The file is not encrypted with
    ///     <c>IDataProtector</c>, like the self-signed TLS certificate state in <c>Server.Core</c>,
    ///     because it is generated automatically rather than managed by the operator like a TSIG
    ///     or DNSSEC key. It is protected by file permissions (owner read/write) only.
    /// </summary>
    public string CertificateStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "dnscrypt-certificates.json");
}
