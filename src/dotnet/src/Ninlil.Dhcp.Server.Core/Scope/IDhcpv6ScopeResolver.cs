using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This interface decides which <see cref="DhcpScopeEntity" /> a DHCPv6 request belongs to,
///     for one particular kind of identity association within it -- the DHCPv6 analogue of
///     <see cref="IDhcpv4ScopeResolver" />.
/// </summary>
public interface IDhcpv6ScopeResolver
{
    /// <summary>
    ///     This method resolves the scope a request's own identity association of a given kind
    ///     should be served from.
    /// </summary>
    /// <param name="request">The client's own message.</param>
    /// <param name="kind">
    ///     Which kind of scope to resolve -- <see cref="DhcpScopeKindEnum.AddressAssignment" /> for
    ///     an IA_NA, <see cref="DhcpScopeKindEnum.PrefixDelegation" /> for an IA_PD.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching scope, or <see langword="null" /> if none can be determined.</returns>
    Task<DhcpScopeEntity> ResolveAsync(
        Dhcpv6Message request,
        DhcpScopeKindEnum kind,
        CancellationToken cancellationToken = default
    );
}
