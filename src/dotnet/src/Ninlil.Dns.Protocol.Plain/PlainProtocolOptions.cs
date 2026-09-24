// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:Plain</c> section of the application's
///     configuration and controls how <see cref="UdpDnsListener" />/<see cref="TcpDnsListener" />
///     bind and behave.
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
    ///     This property contains the local addresses both the UDP and TCP listeners binds to, comma-separated -- for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep plain DNS off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long a TCP connection may sit completely idle -- no
    ///     complete query received -- before this server closes it. RFC 7766 §6.2.3 recommends a
    ///     server-chosen idle timeout to bound how many otherwise-idle connections it must keep
    ///     open at once.
    /// </summary>
    public TimeSpan TcpIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many TCP connections this server accepts at once. A
    ///     connection beyond this limit is accepted and then immediately closed, rather than left
    ///     to queue indefinitely in the operating system's own backlog.
    /// </summary>
    public int MaxConcurrentTcpConnections { get; set; } = 512;
}
