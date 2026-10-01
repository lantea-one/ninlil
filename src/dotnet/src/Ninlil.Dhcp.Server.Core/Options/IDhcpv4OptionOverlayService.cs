using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This interface applies a scope's custom options and matching tag rules to a DHCPv4 reply,
///     after <see cref="Dhcpv4OptionDeliveryService" /> has set the standard options. See
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" /> and
///     <see cref="DhcpScopeCustomOptionEntity" /> for precedence. The implementation is
///     <see cref="Dhcpv4OptionOverlayService" />.
/// </summary>
public interface IDhcpv4OptionOverlayService
{
    /// <summary>
    ///     This method applies the enabled custom options of <paramref name="scope" />, then every
    ///     enabled tag rule eligible for it that matches <paramref name="request" />, to
    ///     <paramref name="reply" />.
    /// </summary>
    /// <param name="reply">
    ///     The reply, already carrying the standard options.
    /// </param>
    /// <param name="scope">The scope the request was resolved into.</param>
    /// <param name="request">
    ///     The request, read for the vendor class, user class and Relay Agent Information values
    ///     tag rules match on.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ApplyOverlaysAsync(
        Dhcpv4Message reply,
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );
}
