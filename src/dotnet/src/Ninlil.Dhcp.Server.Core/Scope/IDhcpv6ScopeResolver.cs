using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This interface decides which <see cref="DhcpScopeEntity" /> serves one kind of identity
///     association in a DHCPv6 request, the counterpart of <see cref="IDhcpv4ScopeResolver" />.
///     See <see cref="Dhcpv6ScopeResolver" />.
/// </summary>
public interface IDhcpv6ScopeResolver
{
    /// <summary>
    ///     This method resolves the scope that serves the request's identity associations of the
    ///     given kind.
    /// </summary>
    /// <param name="request">The client's message.</param>
    /// <param name="kind">
    ///     Which kind of scope to resolve: <see cref="DhcpScopeKindEnum.AddressAssignment" /> for
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
