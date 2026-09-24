using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface resolves the single real IPv6 address this server substitutes wherever a
///     configured DHCPv6 option's own value source is
///     <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" /> (see
///     <see cref="Options.Dhcpv6OptionOverlayService" />). Deliberately distinct from
///     <see cref="IDhcpv6ServerIdentifierProvider" />: that provider resolves this server's own
///     DUID -- an opaque identifier, never a usable network address, RFC 8415's own protocol
///     mechanics call for -- while this one resolves a genuine, dialable IPv6 unicast address, the
///     DHCPv6 analogue of <see cref="IDhcpServerIdentifierProvider" />'s own IPv4 address.
/// </summary>
public interface IDhcpv6ServerAddressProvider
{
    /// <summary>
    ///     This property contains the resolved server address, computed once and cached for the
    ///     lifetime of this provider -- see <see cref="Dhcpv6ServerAddressProvider" />'s own doc
    ///     comment for exactly how it is resolved.
    /// </summary>
    IPAddress Value { get; }
}
