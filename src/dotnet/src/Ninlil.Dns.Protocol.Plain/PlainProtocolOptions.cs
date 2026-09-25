// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:Plain</c> section of the application's
///     configuration and controls how <see cref="UdpDnsListener" /> and
///     <see cref="TcpDnsListener" /> bind and behave. The listeners read it when they start, so a
///     change applies when the listener is restarted.
/// </summary>
public class PlainProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:Plain";

    /// <summary>
    ///     This property contains the port both the UDP and TCP listeners bind to. 53 is the
    ///     IANA-assigned port for DNS.
    /// </summary>
    public int Port { get; set; } = 53;

    /// <summary>
    ///     This property contains the local addresses the UDP and TCP listeners bind to,
    ///     comma-separated: for example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep
    ///     plain DNS off every network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or
    ///     every IPv6 address. Empty (the default) listens on every address of both families
    ///     through one dual-stack socket, which reports IPv4 clients as IPv4-mapped IPv6
    ///     addresses. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is
    ///     one comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long the TCP listener waits for each complete query on a
    ///     connection, including handling it, before closing the connection. RFC 7766 §6.2.3
    ///     recommends a server-chosen idle timeout to limit how many idle connections stay open.
    /// </summary>
    public TimeSpan TcpIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TCP connections the listener serves at once, across
    ///     all listen addresses. A connection beyond the limit is accepted and closed at once,
    ///     rather than left to queue in the operating system's backlog.
    /// </summary>
    public int MaxConcurrentTcpConnections { get; set; } = 512;
}
