using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This interface supplies the DHCPv6 relay's link address: the value written into the
///     "link-address" field of every RELAY-FORW (RFC 8415). It is the DHCPv6 counterpart of
///     <see cref="IDhcpv4RelayAgentAddressProvider" />.
/// </summary>
public interface IDhcpv6RelayAgentAddressProvider
{
    /// <summary>
    ///     This property contains the relay's link address, resolved once at startup.
    /// </summary>
    IPAddress Value { get; }
}
