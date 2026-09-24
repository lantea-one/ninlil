using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This interface hands out reusable, pre-bound UDP sockets for this server's own outbound
///     queries -- the recursive resolver's queries toward authoritative name servers
///     (<c>Server.Recursive</c>) and a forwarder's queries toward a configured upstream resolver
///     (<c>Server.Forwarding</c>) alike. Implemented once, in <c>Server.Core</c> itself, since
///     both of those projects already depend on it -- the same "shared contract lives where every
///     consumer can already reach it" shape this project's other cross-cutting engines follow.
/// </summary>
public interface IOutboundUdpSocketPool
{
    /// <summary>
    ///     This method checks out one socket for the exclusive use of a single query's own
    ///     send-then-receive round trip. The returned <see cref="OutboundUdpSocketLease" /> must
    ///     be disposed once that round trip finishes (successfully or not) so its own socket can
    ///     be returned to the pool for the next caller -- most naturally via a <see langword="using" />
    ///     block around the query's own send/receive logic.
    /// </summary>
    /// <param name="family">Which address family (IPv4 or IPv6) the socket must be bound for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A leased socket, ready to send from and receive into.</returns>
    ValueTask<OutboundUdpSocketLease> RentAsync(
        AddressFamily family,
        CancellationToken stoppingToken = default
    );
}
