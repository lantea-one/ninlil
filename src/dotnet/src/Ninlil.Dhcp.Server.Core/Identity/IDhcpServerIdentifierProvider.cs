using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface resolves the single IPv4 address this server presents as its own Server
///     Identifier option (RFC 2132 Section 9.7) value.
/// </summary>
public interface IDhcpServerIdentifierProvider
{
    /// <summary>
    ///     This property contains the resolved server identifier address, computed once and
    ///     cached for the lifetime of this provider -- see
    ///     <see cref="DhcpServerOptions.ServerIdentifierAddress" />'s own doc comment for why this
    ///     is resolved once rather than per-request.
    /// </summary>
    IPAddress Value { get; }
}
