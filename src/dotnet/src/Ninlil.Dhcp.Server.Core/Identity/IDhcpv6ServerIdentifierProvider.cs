// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface exposes this server's DHCPv6 DUID (DHCP Unique Identifier), which
///     <c>Dhcpv6RequestHandler</c> sends in the Server Identifier option and checks against
///     requests addressed to a server. It is the DHCPv6 counterpart of
///     <see cref="IDhcpServerIdentifierProvider" />; see <see cref="Dhcpv6ServerIdentifierProvider" />.
/// </summary>
public interface IDhcpv6ServerIdentifierProvider
{
    /// <summary>
    ///     This property contains the server's DUID as the raw bytes of a Server Identifier
    ///     option value.
    /// </summary>
    byte[] Value { get; }
}
