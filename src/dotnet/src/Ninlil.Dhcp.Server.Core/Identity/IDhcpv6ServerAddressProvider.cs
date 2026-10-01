using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This interface resolves the IPv6 address this server substitutes for a DHCPv6 option whose
///     value source is <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" />
///     (see <see cref="Options.Dhcpv6OptionOverlayService" />). It is separate from
///     <see cref="IDhcpv6ServerIdentifierProvider" />, which provides the DUID, an opaque
///     identifier rather than an address. It is the IPv6 counterpart of
///     <see cref="IDhcpServerIdentifierProvider" />.
/// </summary>
public interface IDhcpv6ServerAddressProvider
{
    /// <summary>
    ///     This property contains the server address, resolved once and cached (see
    ///     <see cref="Dhcpv6ServerAddressProvider" /> for how).
    /// </summary>
    IPAddress Value { get; }
}
