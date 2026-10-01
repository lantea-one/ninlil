// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains the Neighbor Discovery option codes (RFC 4861, RFC 4191, RFC 8106)
///     the Router Advertisement codec understands. All of them use the RFC 4861 Section 4.6
///     framing: a one-byte type and a one-byte length in units of 8 octets, header included.
///     DHCPv6 options use a 2-byte code and a 2-byte length that excludes the header, so the two
///     codecs are separate.
/// </summary>
public enum RaOptionCodeEnum : byte
{
    /// <summary>
    ///     RFC 4861 Section 4.6.1's Source Link-Layer Address option: the sender's
    ///     link-layer (MAC) address, carried on both a Router Solicitation (when sent from a
    ///     non-unspecified address) and a Router Advertisement.
    /// </summary>
    SourceLinkLayerAddress = 1,

    /// <summary>
    ///     RFC 4861 Section 4.6.2's Prefix Information option: an on-link prefix, and whether
    ///     hosts should autoconfigure an address from it via RFC 4862 SLAAC. A Router Advertisement
    ///     can carry more than one.
    /// </summary>
    PrefixInformation = 3,

    /// <summary>
    ///     RFC 4861 Section 4.6.4's MTU option: the link MTU, for a host that cannot learn
    ///     it any other way.
    /// </summary>
    Mtu = 5,

    /// <summary>
    ///     RFC 4191 Section 2.3's Route Information option: one route this router advertises (a
    ///     prefix, a preference and a lifetime), separate from the on-link and SLAAC role of
    ///     <see cref="PrefixInformation" />. A Router Advertisement can carry more than one. See
    ///     <see cref="Wire.Ra.RaRouteInformationOption" /> for its variable-length wire format.
    /// </summary>
    RouteInformation = 24,

    /// <summary>
    ///     RFC 8106 Section 5.1's Recursive DNS Server (RDNSS) option: one or more DNS resolver
    ///     addresses for a SLAAC-configured host, since RFC 4862 SLAAC carries no DNS
    ///     configuration.
    /// </summary>
    RecursiveDnsServer = 25,

    /// <summary>
    ///     RFC 8106 Section 5.2's DNS Search List (DNSSL) option: one or more search domain
    ///     suffixes for a SLAAC-configured host, the companion of RDNSS. Its value holds domain
    ///     names encoded per RFC 1035 Section 3.1 with compression forbidden; see
    ///     <see cref="Wire.Ra.RaDnsSearchListOption" /> for the encoder and decoder.
    /// </summary>
    DnsSearchList = 31,
}
