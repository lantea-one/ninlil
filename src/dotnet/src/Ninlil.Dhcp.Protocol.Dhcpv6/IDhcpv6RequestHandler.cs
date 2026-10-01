using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This interface decides how a received client/server DHCPv6 message is answered: address
///     assignment, prefix delegation and every other server decision. It is the counterpart of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.IDhcpv4RequestHandler</c>. The implementation is
///     <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c> or, in relay mode,
///     <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c>.
/// </summary>
public interface IDhcpv6RequestHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received DHCPv6 message should be answered.
    /// </summary>
    /// <param name="request">The decoded message a client sent.</param>
    /// <param name="remoteEndPoint">The address and port the request came from.</param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The message to send back, or <see langword="null" /> for no reply, which is a normal
    ///     outcome (a message addressed to another server, for example), not a failure.
    /// </returns>
    ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
