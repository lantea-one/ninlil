using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This interface applies every enabled tag-based conditional option delivery rule
///     (<see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" />) eligible for a scope
///     and matching a DHCPv6 request onto an outgoing reply message, strictly after
///     <see cref="Dhcpv6OptionDeliveryService" />'s own standard options have already been set --
///     the DHCPv6 analogue of <see cref="IDhcpv4OptionOverlayService" />. Unlike its DHCPv4
///     counterpart, this interface does not also apply a scope's own custom options
///     (<see cref="DhcpScopeCustomOptionEntity" />) -- DHCPv6 has no custom-option delivery
///     surface at all today, a separate, pre-existing gap this interface does not close.
/// </summary>
public interface IDhcpv6OptionOverlayService
{
    /// <summary>
    ///     This method applies every enabled tag rule eligible for <paramref name="scope" /> and
    ///     matching <paramref name="request" />, in <see cref="DhcpTagRuleEntity" />'s own
    ///     documented priority order, onto <paramref name="reply" />. A <see langword="null" />
    ///     <paramref name="scope" /> (no scope was resolved for this exchange at all) is tolerated
    ///     by applying nothing, the same way <see cref="Dhcpv6OptionDeliveryService" />'s own
    ///     scope-dependent options are silently skipped in that case.
    /// </summary>
    /// <param name="reply">
    ///     The reply message to apply overlays onto -- already carrying the scope's own standard
    ///     options.
    /// </param>
    /// <param name="scope">The scope the request was resolved into, or <see langword="null" />.</param>
    /// <param name="request">
    ///     The original request, consulted for the Vendor Class/User Class/relayed Interface-ID/
    ///     Remote-ID values a tag rule's own match criteria may need.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ApplyOverlaysAsync(
        Dhcpv6Message reply,
        DhcpScopeEntity scope,
        Dhcpv6Message request,
        CancellationToken cancellationToken = default
    );
}
