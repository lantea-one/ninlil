using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This interface applies the enabled tag rules
///     (<see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" />) eligible for a scope
///     and matching a DHCPv6 request to the reply, after <see cref="Dhcpv6OptionDeliveryService" />
///     has set the standard options. It is the counterpart of <see cref="IDhcpv4OptionOverlayService" />,
///     but applies no scope custom options (<see cref="DhcpScopeCustomOptionEntity" />), which
///     are not delivered over DHCPv6. The implementation is <see cref="Dhcpv6OptionOverlayService" />.
/// </summary>
public interface IDhcpv6OptionOverlayService
{
    /// <summary>
    ///     This method applies every enabled tag rule eligible for <paramref name="scope" /> that
    ///     matches <paramref name="request" />, in the order described on
    ///     <see cref="DhcpTagRuleEntity" />, to <paramref name="reply" />. With a
    ///     <see langword="null" /> <paramref name="scope" /> nothing is applied.
    /// </summary>
    /// <param name="reply">
    ///     The reply, already carrying the standard options.
    /// </param>
    /// <param name="scope">The scope the request was resolved into, or <see langword="null" />.</param>
    /// <param name="request">
    ///     The request, read for the vendor class, user class, Interface-ID and Remote-ID values
    ///     tag rules match on.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ApplyOverlaysAsync(
        Dhcpv6Message reply,
        DhcpScopeEntity scope,
        Dhcpv6Message request,
        CancellationToken cancellationToken = default
    );
}
