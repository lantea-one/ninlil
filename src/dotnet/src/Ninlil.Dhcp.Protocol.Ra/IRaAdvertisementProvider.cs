using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This interface decides what a Router Advertisement contains: M/O flags, prefixes, routes,
///     MTU and DNS options. <see cref="Icmpv6RaListener" /> asks it before every send, unsolicited
///     (RFC 4861 Section 6.2.4) or in answer to a <see cref="RouterSolicitationMessage" /> (RFC
///     4861 Section 6.2.6). It is the Router Advertisement counterpart of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.IDhcpv6RequestHandler</c>, without per-client state: every
///     host on an interface gets the same advertisement. The implementation is
///     <c>Ninlil.Dhcp.Server.Ra.RaAdvertisementProvider</c>.
/// </summary>
public interface IRaAdvertisementProvider
{
    /// <summary>
    ///     This method builds the advertisement to send now on one interface. The listener calls
    ///     it for every send.
    /// </summary>
    /// <param name="interfaceName">
    ///     The name (<see cref="System.Net.NetworkInformation.NetworkInterface.Name" />) of the
    ///     interface the advertisement is for, used to find its
    ///     <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c> entry. Fields the entry leaves
    ///     unset use the global values, and an entry with <c>Exclude</c> set makes the method
    ///     return <see langword="null" />. <see langword="null" /> (the default) means the
    ///     interface is unknown: the global values are used and no override or exclusion applies.
    /// </param>
    /// <returns>
    ///     The advertisement to send, or <see langword="null" /> to send nothing: when the
    ///     interface is excluded, or when the effective configuration has nothing to say (no
    ///     prefixes, routes, DNS options or MTU, neither M nor O flag, and a zero router lifetime).
    /// </returns>
    RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null);
}
