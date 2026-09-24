using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This interface decides which <see cref="DhcpScopeEntity" /> a received DHCPv4 message
///     belongs to -- the question every message type must answer before
///     <see cref="Allocation.IDhcpv4LeaseAllocationService" /> can do anything, since a scope's
///     own address range and reservations are all this engine ever draws from.
/// </summary>
public interface IDhcpv4ScopeResolver
{
    /// <summary>
    ///     This method resolves the scope a request belongs to. See
    ///     <see cref="Dhcpv4ScopeResolver" />'s own doc comment for the full resolution order.
    /// </summary>
    /// <param name="request">The request to resolve a scope for.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching scope, or <see langword="null" /> if none can be determined.</returns>
    Task<DhcpScopeEntity> ResolveAsync(
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );
}
