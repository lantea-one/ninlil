using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverTls</c> section of the
///     application's configuration and controls how <see cref="DnsOverTlsListener" /> binds and
///     behaves. The listener reads <see cref="IdleTimeout" />, the allowed TLS protocols and
///     <c>ProxyProtocol.Enable</c> from <c>IOptionsMonitor</c> for each connection. Everything
///     else is read when the listener starts and applies after a restart: the listen addresses,
///     the port, the connection limit, the certificate and the <c>ProxyProtocol</c> known
///     proxies and networks, which are parsed once at start. The REST settings controller
///     (<c>ReverseProxySettingsController</c>) reads these options through <c>IOptions</c>, so
///     a value it shows or changes is current only after a process restart.
/// </summary>
public class DnsOverTlsOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverTls";

    /// <summary>
    ///     This property controls whether this transport listens at all. It is off by default: a
    ///     new listening port and a certificate to manage are changes an operator should opt into,
    ///     as with <c>EnableRecursion</c>. (Self-maintaining features, such as RFC 5011 trust
    ///     anchor rollover, default on instead.)
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to. 853 is the IANA-assigned
    ///     port for DNS-over-TLS.
    /// </summary>
    public int Port { get; set; } = 853;

    /// <summary>
    ///     This property contains the local addresses this transport binds to, comma-separated: for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNS-over-TLS off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long the listener waits for each complete query on a TLS
    ///     connection, including handling it, before closing the connection, for the reason RFC
    ///     7766 §6.2.3 gives for DNS over TCP. It does not cover the TLS handshake.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TLS connections the listener serves at once, across all
    ///     listen addresses; a connection beyond the limit is closed at once.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 512;

    /// <summary>
    ///     This property contains this transport's server certificate configuration.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();

    /// <summary>
    ///     This property contains this transport's PROXY protocol trust configuration (see
    ///     <see cref="ProxyProtocolOptions" />).
    /// </summary>
    public ProxyProtocolOptions ProxyProtocol { get; set; } = new();
}
