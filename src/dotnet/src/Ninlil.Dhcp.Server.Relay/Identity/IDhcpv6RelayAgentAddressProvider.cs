using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This interface resolves this server's own RFC 8415 Section 7.1 relay-agent link address --
///     the value written into every RELAY-FORW's own "link-address" field -- the DHCPv6 analogue
///     of <see cref="IDhcpv4RelayAgentAddressProvider" />.
/// </summary>
public interface IDhcpv6RelayAgentAddressProvider
{
    /// <summary>
    ///     This property contains this relay's own address, resolved once at startup.
    /// </summary>
    IPAddress Value { get; }
}
