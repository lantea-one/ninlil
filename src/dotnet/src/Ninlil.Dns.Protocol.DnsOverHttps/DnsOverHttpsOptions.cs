using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverHttps</c> section of the
///     application's configuration and controls how <see cref="DnsOverHttpsHostedService" />
///     binds and behaves.
/// </summary>
public class DnsOverHttpsOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverHttps";

    /// <summary>
    ///     This property controls whether this transport listens at all. Off by default, for the
    ///     same reason as <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions.Enable</c> --
    ///     a new listening port and a certificate to manage is a real, visible configuration
    ///     change an operator should opt into.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this transport binds to. 443 is the conventional port
    ///     every major public DoH resolver (Cloudflare, Google, Quad9) actually listens on, so
    ///     DoH traffic blends in with ordinary HTTPS traffic on the wire.
    /// </summary>
    public int Port { get; set; } = 443;

    /// <summary>
    ///     This property contains the HTTP path both the GET and POST forms of a DoH query are
    ///     answered at, per RFC 8484 §4.1. <c>/dns-query</c> is not mandated by the RFC, but is
    ///     what every major public DoH resolver actually uses.
    /// </summary>
    public string Path { get; set; } = "/dns-query";

    /// <summary>
    ///     This property bounds how large a single DNS message (request or response) this
    ///     transport accepts may be -- 65535 is the maximum any DNS message can ever legally be,
    ///     per the 16-bit RDLENGTH/message-length fields RFC 1035 itself uses.
    /// </summary>
    public int MaxMessageSizeBytes { get; set; } = 65535;

    /// <summary>
    ///     This property contains this transport's own server certificate configuration.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
