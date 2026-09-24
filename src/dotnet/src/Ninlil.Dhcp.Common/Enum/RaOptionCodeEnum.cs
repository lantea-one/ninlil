// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains the RFC 4861/RFC 8106 Neighbor Discovery option codes this
///     project's Router Advertisement wire codec actually understands. Every value here shares
///     the same one-byte type/one-byte length (in units of 8 octets, including that 2-byte
///     header itself) framing RFC 4861 Section 4.6 defines -- a genuinely different option
///     framing from DHCPv6's own 2-byte code/2-byte length (excluding its own header), which is
///     why this project's Router Advertisement codec is a separate implementation from
///     <see cref="Dhcpv6OptionCodeEnum" />'s own, not a shared one.
/// </summary>
public enum RaOptionCodeEnum : byte
{
    /// <summary>
    ///     RFC 4861 Section 4.6.1's Source Link-Layer Address option -- the sender's own
    ///     link-layer (MAC) address, carried on both a Router Solicitation (when sent from a
    ///     non-unspecified address) and a Router Advertisement.
    /// </summary>
    SourceLinkLayerAddress = 1,

    /// <summary>
    ///     RFC 4861 Section 4.6.2's Prefix Information option -- an on-link prefix, and whether
    ///     hosts should autoconfigure an address from it via RFC 4862 SLAAC. A Router Advertisement
    ///     can carry more than one.
    /// </summary>
    PrefixInformation = 3,

    /// <summary>
    ///     RFC 4861 Section 4.6.4's MTU option -- the link's own MTU, for a host that cannot learn
    ///     it any other way.
    /// </summary>
    Mtu = 5,

    /// <summary>
    ///     RFC 4191 Section 2.3's Route Information option -- one specific route this router
    ///     advertises (a prefix, a preference, and how long the route stays valid), distinct from
    ///     the on-link/SLAAC purpose <see cref="PrefixInformation" /> serves. A Router
    ///     Advertisement can carry more than one, exactly like <see cref="PrefixInformation" />.
    ///     See <see cref="Wire.Ra.RaRouteInformationOption" />'s own doc comment for this option's
    ///     own variable-length wire shape.
    /// </summary>
    RouteInformation = 24,

    /// <summary>
    ///     RFC 8106 Section 5.1's Recursive DNS Server (RDNSS) option -- one or more DNS resolver
    ///     addresses a SLAAC-configured host should use, since RFC 4862 SLAAC itself carries no DNS
    ///     configuration of its own.
    /// </summary>
    RecursiveDnsServer = 25,

    /// <summary>
    ///     RFC 8106 Section 5.2's DNS Search List (DNSSL) option -- one or more unqualified-name
    ///     search domain suffixes a SLAAC-configured host should try, the RDNSS option's own
    ///     counterpart. Unlike every other option this enumeration lists, its own value carries
    ///     domain names encoded per RFC 1035 Section 3.1 but with message compression explicitly
    ///     forbidden (RFC 8106 Section 5.2) -- see <see cref="Wire.Ra.RaDnsSearchListOption" />'s
    ///     own doc comment for the dedicated, compression-free encoder/decoder this requires.
    /// </summary>
    DnsSearchList = 31,
}
