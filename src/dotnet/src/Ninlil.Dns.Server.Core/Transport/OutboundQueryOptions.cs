// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class binds to the <c>DnsServer:OutboundQuery</c> section of the application's
///     configuration and controls <see cref="OutboundUdpSocketPool" />, which supplies the source
///     sockets for outbound UDP queries: the recursive resolver's queries to authoritative
///     servers and the forwarder's queries to upstream resolvers.
/// </summary>
public sealed class OutboundQueryOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:OutboundQuery";

    /// <summary>
    ///     This property controls whether outbound UDP queries reuse sockets from
    ///     <see cref="OutboundUdpSocketPool" />. Off by default. When off, each query in
    ///     <c>Ninlil.Dns.Server.Recursive.Transport.UpstreamDnsClient</c> and
    ///     <c>Ninlil.Dns.Server.Forwarding.Clients.PlainForwarderClient</c> gets a new socket, so
    ///     the OS picks a fresh random source port every time (RFC 5452; ports in
    ///     <see cref="ExcludedSourcePorts" /> are still avoided). Turn it on only when binding a
    ///     socket per query costs too much. When on, queries rotate across a fixed set of at most
    ///     <see cref="PoolSizePerAddressFamily" /> source ports, which offers fewer possible ports.
    /// </summary>
    public bool EnableSourcePortPool { get; set; } = false;

    /// <summary>
    ///     This property contains how many sockets the pool keeps per address family (IPv4 and
    ///     IPv6 are capped separately), which is how many source ports outbound queries rotate
    ///     across. A query that finds every socket of its family in use waits for one to be
    ///     returned. The value is read when a family is first used, so a change needs a restart.
    /// </summary>
    public int PoolSizePerAddressFamily { get; set; } = 128;

    /// <summary>
    ///     This property contains local source ports outbound sockets must not use, such as a port
    ///     another local service listens on. Empty by default. The check is best effort: after
    ///     25 failed attempts the socket takes whatever port the OS assigns.
    /// </summary>
    public List<int> ExcludedSourcePorts { get; set; } = [];
}
