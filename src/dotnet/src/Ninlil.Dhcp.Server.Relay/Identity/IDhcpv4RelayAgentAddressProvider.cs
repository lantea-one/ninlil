using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This interface supplies the relay agent's address: the value written into <c>giaddr</c>
///     when this relay is the first to forward a request, and the <c>giaddr</c> a reply must carry
///     to be relayed to a client.
/// </summary>
public interface IDhcpv4RelayAgentAddressProvider
{
    /// <summary>
    ///     This property contains the relay's address, resolved once at startup.
    /// </summary>
    IPAddress Value { get; }
}
