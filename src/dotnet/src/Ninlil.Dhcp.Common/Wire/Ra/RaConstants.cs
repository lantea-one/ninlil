// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class holds the fixed sizes of the RFC 4861 Router Solicitation and Advertisement
///     wire format, shared by <see cref="RaMessageReader" /> and <see cref="RaMessageWriter" /> so
///     they cannot drift apart. It is the Router Advertisement counterpart of
///     <see cref="Dhcpv6Constants" />.
/// </summary>
public static class RaConstants
{
    /// <summary>
    ///     The length, in bytes, of the ICMPv6 header: a 1-byte "type", a 1-byte "code" (zero for
    ///     both message types here) and a 2-byte checksum.
    /// </summary>
    public const int Icmpv6HeaderLength = 4;

    /// <summary>
    ///     The length, in bytes, of the RFC 4861 Section 4.1 Router Solicitation body (one 4-byte
    ///     reserved field) before the options.
    /// </summary>
    public const int RouterSolicitationFixedBodyLength = 4;

    /// <summary>
    ///     The length, in bytes, of the RFC 4861 Section 4.2 Router Advertisement body before the
    ///     options: Cur Hop Limit (1 byte), the M/O flags and 6 reserved bits (1 byte), Router
    ///     Lifetime (2 bytes), Reachable Time (4 bytes) and Retrans Timer (4 bytes).
    /// </summary>
    public const int RouterAdvertisementFixedBodyLength = 12;

    /// <summary>
    ///     The length, in bytes, of a Neighbor Discovery option header: a 1-byte type and a 1-byte
    ///     length. Unlike <see cref="Dhcpv6Constants.OptionHeaderLength" />, the RFC 4861 Section
    ///     4.6 length counts 8-octet units and includes this header.
    /// </summary>
    public const int OptionHeaderLength = 2;

    /// <summary>
    ///     The number of bytes one unit of a Neighbor Discovery option's "length" field stands for
    ///     (RFC 4861 Section 4.6).
    /// </summary>
    public const int OptionLengthUnitBytes = 8;
}
