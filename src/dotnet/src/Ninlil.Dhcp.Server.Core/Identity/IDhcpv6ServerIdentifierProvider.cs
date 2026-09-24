// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface exposes this server's own DHCPv6 DUID (DHCP Unique Identifier), resolved
///     once at startup -- the DHCPv6 analogue of <see cref="IDhcpServerIdentifierProvider" />.
/// </summary>
public interface IDhcpv6ServerIdentifierProvider
{
    /// <summary>
    ///     This property contains this server's own DUID, as raw bytes exactly as they belong in
    ///     a Server Identifier option's own value.
    /// </summary>
    byte[] Value { get; }
}
