using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This interface decides how a received DHCPv4 message is answered: lease allocation,
///     reservations and every other server decision. <see cref="Dhcpv4UdpListener" /> only decodes
///     datagrams, calls it and sends back the result. <c>Ninlil.Dhcp.Server.Core</c> provides the
///     implementation (<c>Dhcpv4RequestHandler</c>); the transport tests use a stub.
/// </summary>
public interface IDhcpv4RequestHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received DHCPv4 message should be answered.
    /// </summary>
    /// <param name="request">The decoded message a client (or a relay agent, on its behalf) sent.</param>
    /// <param name="remoteEndPoint">
    ///     The address the datagram came from: the relay agent's when one forwarded it, not
    ///     necessarily the client's.
    /// </param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The message to send back, or <see langword="null" /> for no reply, which is a normal
    ///     outcome (a DISCOVER when the pool is exhausted, for example), not a failure.
    /// </returns>
    ValueTask<Dhcpv4Message> HandleRequestAsync(
        Dhcpv4Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
