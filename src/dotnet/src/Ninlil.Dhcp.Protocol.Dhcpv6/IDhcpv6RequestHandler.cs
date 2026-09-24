using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This interface is what actually decides how a received DHCPv6 message should be answered
///     -- stateful address assignment, Prefix Delegation, and every other real server-engine
///     decision. <see cref="Dhcpv6UdpListener" /> knows nothing about any of that; it only
///     decodes a datagram, hands the result to whichever implementation of this interface
///     dependency injection resolves, and sends back whatever (if anything) comes back -- the
///     DHCPv6 analogue of <c>Ninlil.Dhcp.Protocol.Dhcpv4.IDhcpv4RequestHandler</c>.
/// </summary>
public interface IDhcpv6RequestHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received DHCPv6 message should be answered.
    /// </summary>
    /// <param name="request">The decoded message a client sent.</param>
    /// <param name="remoteEndPoint">The socket address the request actually arrived from.</param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The message to send back, or <see langword="null" /> if this request should receive no
    ///     reply at all -- an ordinary, silent outcome (a SOLICIT when no server has anything to
    ///     offer, per RFC 8415 Section 18.3.9, is answered with silence, not a REPLY), never itself
    ///     a failure.
    /// </returns>
    ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
