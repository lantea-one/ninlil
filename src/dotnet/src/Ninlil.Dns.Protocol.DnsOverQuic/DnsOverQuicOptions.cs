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
    ///     This property contains the local addresses this transport binds to, comma-separated -- for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNS-over-QUIC off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    ///     <para>
    ///         QUIC differs from the socket-based transports in two ways, both down to the QUIC
    ///         library (MsQuic) owning its own sockets: <c>::</c> also answers IPv4 clients, and the
    ///         UDP port is still reserved on every address even when only some are listed -- the
    ///         library binds a wildcard socket and filters by address itself. Listing addresses
    ///         keeps DNS-over-QUIC from answering on the others, but another program can't use
    ///         the same UDP port on an unlisted address.
    ///     </para>
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

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
