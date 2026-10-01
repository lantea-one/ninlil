using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface resolves the IPv4 address this server sends as its Server Identifier option
///     (RFC 2132 Section 9.7). The DHCPv4 engine also uses it as the default boot server address
///     and for options whose value source is "server address". See
///     <see cref="DhcpServerIdentifierProvider" />.
/// </summary>
public interface IDhcpServerIdentifierProvider
{
    /// <summary>
    ///     This property contains the server identifier address, resolved once and cached (see
    ///     <see cref="DhcpServerOptions.ServerIdentifierAddress" /> for why it is not resolved per
    ///     request).
    /// </summary>
    IPAddress Value { get; }
}
