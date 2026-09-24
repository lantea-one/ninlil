using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This interface applies a scope's own operator-configured custom options and any matching
///     tag-based conditional option delivery rules onto an outgoing reply message, strictly after
///     <see cref="Dhcpv4OptionDeliveryService" />'s own standard options have already been set --
///     see <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" /> and
///     <see cref="DhcpScopeCustomOptionEntity" />'s own doc comments for the full
///     precedence/conflict-resolution rules this overlay implements.
/// </summary>
public interface IDhcpv4OptionOverlayService
{
    /// <summary>
    ///     This method applies every enabled custom option configured for <paramref name="scope" />,
    ///     then every enabled tag rule eligible for it and matching <paramref name="request" />, in
    ///     that order, onto <paramref name="reply" />.
    /// </summary>
    /// <param name="reply">
    ///     The reply message to apply overlays onto -- already carrying the scope's own standard
    ///     options.
    /// </param>
    /// <param name="scope">The scope the request was resolved into.</param>
    /// <param name="request">
    ///     The original request, consulted for the Vendor Class Identifier/User Class/Relay Agent
    ///     Information values a tag rule's own match criteria may need.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ApplyOverlaysAsync(
        Dhcpv4Message reply,
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );
}
