using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This interface is what actually decides what a Router Advertisement should currently
///     contain -- <see cref="Icmpv6RaListener" /> knows nothing about M/O flags, advertised
///     prefixes, MTU, or DNS configuration; it only asks whichever implementation of this
///     interface dependency injection resolves for the current advertisement, whether it is about
///     to send one unsolicited (RFC 4861 Section 6.2.4) or in response to a real
///     <see cref="RouterSolicitationMessage" /> (RFC 4861 Section 6.2.6) -- the Router
///     Advertisement analogue of <c>Ninlil.Dhcp.Protocol.Dhcpv6.IDhcpv6RequestHandler</c>, made
///     simpler by there being no per-client request/reply state at all: the same advertisement is
///     sent to every host on the link, solicited or not.
/// </summary>
public interface IRaAdvertisementProvider
{
    /// <summary>
    ///     This method builds the Router Advertisement this server should currently be sending on
    ///     one specific interface -- called once per periodic unsolicited send, and once per
    ///     genuine Router Solicitation received.
    /// </summary>
    /// <param name="interfaceName">
    ///     The OS-reported name (<see cref="System.Net.NetworkInformation.NetworkInterface.Name" />)
    ///     of the interface this advertisement is about to be sent on, used to resolve that
    ///     interface's own <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c> entry, if one is
    ///     configured -- every field of that entry left unset falls back to this server's own
    ///     global default content, and a matching entry with its own <c>Exclude</c> flag set makes
    ///     this method return <see langword="null" /> unconditionally. <see langword="null" /> (the
    ///     default) means the caller does not know, or does not care about, which specific
    ///     interface this send is for -- every field then uses this server's own global default
    ///     content verbatim, and no interface override, including an exclusion, can ever apply,
    ///     since there is no specific interface name to match one against.
    /// </param>
    /// <returns>
    ///     The advertisement to send, or <see langword="null" /> if this server has nothing to
    ///     advertise at all right now (Router Advertisement is disabled, this specific interface is
    ///     explicitly excluded, or the effective content is configured with no prefixes and not
    ///     acting as a default router) -- the caller sends nothing in that case.
    /// </returns>
    RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null);
}
