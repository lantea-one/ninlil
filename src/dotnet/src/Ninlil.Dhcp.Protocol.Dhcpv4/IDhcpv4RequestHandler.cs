using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This interface is what actually decides how a received DHCPv4 message should be
///     answered -- lease allocation, static reservations, and every other real server-engine
///     decision. <see cref="Dhcpv4UdpListener" /> knows nothing about any of that; it only
///     decodes a datagram, hands the result to whichever implementation of this interface
///     dependency injection resolves, and sends back whatever (if anything) comes back -- so a
///     real server engine can be dropped in as this interface's implementation without reworking
///     the transport underneath it at all.
/// </summary>
public interface IDhcpv4RequestHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received DHCPv4 message should be answered.
    /// </summary>
    /// <param name="request">The decoded message a client (or a relay agent, on its behalf) sent.</param>
    /// <param name="remoteEndPoint">
    ///     The socket address the request actually arrived from -- a relay agent's own address
    ///     when one is involved, not necessarily the originating client's own address.
    /// </param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The message to send back, or <see langword="null" /> if this request should receive no
    ///     reply at all -- an ordinary, silent outcome for a request this server has nothing
    ///     useful to say about (a DISCOVER when every address in every scope is already leased,
    ///     for example), never itself a failure.
    /// </returns>
    ValueTask<Dhcpv4Message> HandleRequestAsync(
        Dhcpv4Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
