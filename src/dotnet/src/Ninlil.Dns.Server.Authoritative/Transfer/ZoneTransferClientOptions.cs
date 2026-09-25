// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneTransferClient</c> section of the
///     application's configuration and controls how the <see cref="IZoneTransferClient" />s reach
///     a primary: ports, timeouts and certificate checks. They read it on each pull.
/// </summary>
public class ZoneTransferClientOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneTransferClient";

    /// <summary>
    ///     This property contains the primary's port for plain TCP and UDP. It is 53 in practice;
    ///     tests change it to use an unprivileged port.
    /// </summary>
    public int PrimaryPort { get; set; } = 53;

    /// <summary>
    ///     This property contains how long to wait for the initial TCP connection to succeed.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This property contains how long to wait for each message in a transfer response
    ///     before giving up on the whole pull.
    /// </summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how long to wait for a plain SOA query's response.
    /// </summary>
    public TimeSpan SoaQueryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property contains the primary's port for RFC 9103 zone transfer over TLS. The
    ///     default, 853, is the DNS-over-TLS port (RFC 7858); Ninlil's DNS-over-TLS listener serves
    ///     queries and transfers on it.
    /// </summary>
    public int PrimaryTlsPort { get; set; } = 853;

    /// <summary>
    ///     This property contains the primary's port for zone transfer over QUIC. The default, 853,
    ///     is the DNS-over-QUIC port (RFC 9250).
    /// </summary>
    public int PrimaryQuicPort { get; set; } = 853;

    /// <summary>
    ///     This property controls whether a TLS or QUIC pull checks the primary's certificate
    ///     against the operating system trust store. On by default. Turn it off only for a primary
    ///     with a certificate the secondary cannot otherwise trust, such as Ninlil's auto-generated
    ///     self-signed one. Then only TSIG, when a key is configured for the zone, protects the
    ///     content; the connection itself can be intercepted.
    /// </summary>
    public bool ValidateServerCertificate { get; set; } = true;
}
