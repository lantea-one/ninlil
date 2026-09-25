using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This interface hands out bound UDP sockets for outbound queries: the recursive resolver's
///     queries to authoritative servers (<c>Server.Recursive</c>) and the forwarder's queries to
///     upstream resolvers (<c>Server.Forwarding</c>). It lives in <c>Server.Core</c> because both
///     projects reference it. The implementation is <see cref="OutboundUdpSocketPool" />.
/// </summary>
public interface IOutboundUdpSocketPool
{
    /// <summary>
    ///     This method checks out one socket for one query's send and receive, waiting if every
    ///     pooled socket is in use. Dispose the returned <see cref="OutboundUdpSocketLease" /> when
    ///     the query finishes, successfully or not, usually with a <see langword="using" /> block.
    /// </summary>
    /// <param name="family">Which address family (IPv4 or IPv6) the socket must be bound for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A leased socket, ready to send from and receive into.</returns>
    ValueTask<OutboundUdpSocketLease> RentAsync(
        AddressFamily family,
        CancellationToken stoppingToken = default
    );
}
