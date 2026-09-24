// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneTransferClient</c> section of the
///     application's configuration and controls how <see cref="IZoneTransferClient" /> pulls
///     transfers from a primary.
/// </summary>
public class ZoneTransferClientOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneTransferClient";

    /// <summary>
    ///     This property contains the port a primary is assumed to listen on. Always 53 in
    ///     practice; configurable purely so a test can point this at a loopback listener without
    ///     needing the privileges binding port 53 itself would require.
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
    ///     This property contains the port a primary is assumed to listen for RFC 9103 XFR-over-TLS
    ///     connections on. Defaults to 853 -- the same well-known port DNS-over-TLS (RFC 7858)
    ///     already uses, since this project's own <c>Ninlil.Dns.Protocol.DnsOverTls</c> listener
    ///     answers both a stub-resolver query and an AXFR/IXFR request on the same port.
    /// </summary>
    public int PrimaryTlsPort { get; set; } = 853;

    /// <summary>
    ///     This property contains the port a primary is assumed to listen for QUIC-carried
    ///     zone-transfer connections on. Defaults to 853, the same well-known DNS-over-QUIC (RFC
    ///     9250) port <see cref="PrimaryTlsPort" />'s own doc comment explains the reasoning for.
    /// </summary>
    public int PrimaryQuicPort { get; set; } = 853;

    /// <summary>
    ///     This property controls whether an outbound TLS or QUIC zone-transfer pull validates the
    ///     primary's own certificate against the normal OS trust store. Defaults to
    ///     <see langword="true" /> (the safe default for any real deployment reachable by a
    ///     publicly-trusted or internal CA-issued certificate); set to <see langword="false" />
    ///     only for a homelab primary presenting a certificate this secondary has no other way to
    ///     trust (for example, a self-signed one this project's own
    ///     <c>ReloadableTlsCertificateProvider</c> auto-generates when no operator-supplied
    ///     certificate is configured) -- TSIG (<see cref="ZoneTransferClientOptions" />'s own
    ///     caller, <c>SecondaryZoneRefreshService</c>) still authenticates every transfer either
    ///     way, so disabling certificate validation here trades away transport-level
    ///     man-in-the-middle protection, not authentication of the zone content itself.
    /// </summary>
    public bool ValidateServerCertificate { get; set; } = true;
}
