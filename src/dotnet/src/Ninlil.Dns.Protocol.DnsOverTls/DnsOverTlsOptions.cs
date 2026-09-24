using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverTls</c> section of the
///     application's configuration and controls how <see cref="DnsOverTlsListener" /> binds and
///     behaves.
/// </summary>
public class DnsOverTlsOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverTls";

    /// <summary>
    ///     This property controls whether this transport listens at all. Off by default -- a new
    ///     listening port and a certificate to manage is a real, visible configuration change an
    ///     operator should opt into, the same posture <c>EnableRecursion</c> already takes for a
    ///     new capability (as opposed to the automated-*maintenance* toggles this project also
    ///     has, like RFC 5011 trust anchor rollover, which default on instead).
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to. 853 is the IANA-assigned
    ///     port for DNS-over-TLS.
    /// </summary>
    public int Port { get; set; } = 853;

    /// <summary>
    ///     This property contains the local addresses this transport binds to, comma-separated -- for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNS-over-TLS off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long a TLS connection may sit completely idle -- no
    ///     complete query received -- before this server closes it, the same reasoning RFC 7766
    ///     §6.2.3 gives for plain DNS-over-TCP.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TLS connections this server accepts at once.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 512;

    /// <summary>
    ///     This property contains this transport's own server certificate configuration.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();

    /// <summary>
    ///     This property contains this transport's own PROXY protocol trust configuration -- see
    ///     <see cref="ProxyProtocolOptions" />'s own doc comment for the full reasoning.
    /// </summary>
    public ProxyProtocolOptions ProxyProtocol { get; set; } = new();
}
