using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This interface resolves this server's own RFC 3046 relay-agent address -- the value
///     written into <c>giaddr</c> on every request this relay is the first relay to forward.
/// </summary>
public interface IDhcpv4RelayAgentAddressProvider
{
    /// <summary>
    ///     This property contains this relay's own address, resolved once at startup.
    /// </summary>
    IPAddress Value { get; }
}
