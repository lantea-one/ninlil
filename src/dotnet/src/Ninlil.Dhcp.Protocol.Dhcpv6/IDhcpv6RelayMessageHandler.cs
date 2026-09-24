using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This interface decides how a received RELAY-FORW/RELAY-REPL message should be answered --
///     the relay-message sibling of <see cref="IDhcpv6RequestHandler" />, which only ever sees an
///     ordinary client/server message. <see cref="Dhcpv6UdpListener" /> resolves this as an
///     <b>optional</b> dependency (via <see cref="IServiceProvider.GetService" />, not a required
///     constructor parameter) -- a listener with nothing registered for this interface simply
///     discards a relay message it happens to receive, logging once, rather than failing to start
///     at all.
///     <para>
///         Two genuinely different implementations exist, corresponding to this project's own two
///         relay-adjacent roles: <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c> implements
///         this to unwrap a RELAY-FORW an external relay agent forwarded to this server, process
///         the inner client message exactly as it would an unrelayed one, and wrap the result back
///         up as a RELAY-REPL; <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c> implements this to
///         unwrap a RELAY-REPL its own upstream server sent back, and deliver the inner reply down
///         to the original client. The composition root registers exactly one of the two,
///         mirroring how exactly one <see cref="IDhcpv6RequestHandler" /> is ever registered too.
///     </para>
/// </summary>
public interface IDhcpv6RelayMessageHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received RELAY-FORW/RELAY-REPL message should be
    ///     answered.
    /// </summary>
    /// <param name="message">The decoded relay message.</param>
    /// <param name="remoteEndPoint">The socket address the message actually arrived from.</param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The relay message to send back to <paramref name="remoteEndPoint" />, or
    ///     <see langword="null" /> if nothing should be sent back from there at all -- for example,
    ///     because this implementation already sent everything it needed to through a socket of its
    ///     own (<c>Dhcpv6RelayAgent</c>'s own posture), or because there is genuinely nothing
    ///     sensible to answer with.
    /// </returns>
    Task<Dhcpv6RelayMessage> HandleRelayMessageAsync(
        Dhcpv6RelayMessage message,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
