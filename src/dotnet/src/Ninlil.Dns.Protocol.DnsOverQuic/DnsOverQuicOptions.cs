using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverQuic</c> section of the
///     application's configuration and controls how <see cref="DnsOverQuicListener" /> binds
///     and behaves.
/// </summary>
public class DnsOverQuicOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverQuic";

    /// <summary>
    ///     This property controls whether this transport listens at all. Off by default, the
    ///     same reasoning as <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions.Enable</c>
    ///     -- a new listening port and a certificate to manage is a real, visible configuration
    ///     change an operator should opt into.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to. 853 is the IANA-assigned
    ///     port for DNS-over-QUIC, per RFC 9250 -- the same number DNS-over-TLS uses, since UDP
    ///     and TCP are separate namespaces at the same port number and both protocols were
    ///     deliberately assigned it.
    /// </summary>
    public int Port { get; set; } = 853;

    /// <summary>
    ///     This property contains how long a QUIC connection may sit completely idle before this
    ///     server closes it.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property bounds how many bidirectional streams -- one per in-flight query, per
    ///     RFC 9250 §4.2 -- a single QUIC connection may have open at once.
    /// </summary>
    public int MaxConcurrentBidirectionalStreams { get; set; } = 100;

    /// <summary>
    ///     This property contains this transport's own server certificate configuration.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
