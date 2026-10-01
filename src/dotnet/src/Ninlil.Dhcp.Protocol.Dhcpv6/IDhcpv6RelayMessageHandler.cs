using System.Net;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This interface decides how a received RELAY-FORW or RELAY-REPL is answered; it is the
///     relay counterpart of <see cref="IDhcpv6RequestHandler" />, which sees only client/server
///     messages. <see cref="Dhcpv6UdpListener" /> resolves it as an optional service (through
///     <see cref="IServiceProvider.GetService" />): with none registered, relay messages are
///     discarded and logged at debug level.
///     <para>
///         There are two implementations, one per role. <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c>
///         unwraps a RELAY-FORW from an external relay agent, handles the client message as if
///         unrelayed and wraps the reply in a RELAY-REPL. <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c>
///         unwraps a RELAY-REPL from its upstream server and delivers the reply to the client. The
///         composition root (<c>Program.cs</c>) registers one or the other depending on
///         <c>Dhcpv6RelayOptions.Enable</c>, together with the matching
///         <see cref="IDhcpv6RequestHandler" />.
///     </para>
/// </summary>
public interface IDhcpv6RelayMessageHandler
{
    /// <summary>
    ///     This method decides how, if at all, a received RELAY-FORW or RELAY-REPL is answered.
    /// </summary>
    /// <param name="message">The decoded relay message.</param>
    /// <param name="remoteEndPoint">The address and port the message came from.</param>
    /// <param name="cancellationToken">A token that becomes signaled once the listener is stopping.</param>
    /// <returns>
    ///     The relay message to send back to <paramref name="remoteEndPoint" />, or
    ///     <see langword="null" /> to send nothing, for example because the implementation has
    ///     already sent what it needed on another socket (as <c>Dhcpv6RelayAgent</c> does) or there
    ///     is nothing to answer.
    /// </returns>
    Task<Dhcpv6RelayMessage> HandleRelayMessageAsync(
        Dhcpv6RelayMessage message,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    );
}
