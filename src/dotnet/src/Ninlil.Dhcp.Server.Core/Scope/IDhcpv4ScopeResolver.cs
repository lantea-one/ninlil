using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This interface decides which <see cref="DhcpScopeEntity" /> a received DHCPv4 message
///     belongs to. <see cref="Dhcpv4RequestHandler" /> asks it first, since every address,
///     reservation and option comes from a scope.
/// </summary>
public interface IDhcpv4ScopeResolver
{
    /// <summary>
    ///     This method resolves the scope a request belongs to (see
    ///     <see cref="Dhcpv4ScopeResolver" /> for the order of checks).
    /// </summary>
    /// <param name="request">The request to resolve a scope for.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching scope, or <see langword="null" /> if none can be determined.</returns>
    Task<DhcpScopeEntity> ResolveAsync(
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );
}
