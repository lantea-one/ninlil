// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class holds the fixed sizes RFC 4861's Router Solicitation/Advertisement wire format
///     is built from, shared between the reader and writer classes so the two can never quietly
///     drift out of sync with each other -- the Router Advertisement analogue of
///     <see cref="Dhcpv6Constants" />.
/// </summary>
public static class RaConstants
{
    /// <summary>
    ///     The length, in bytes, of the ICMPv6 header every message this codec reads or writes
    ///     shares -- a 1-byte "type", a 1-byte "code" (always zero for both message types this
    ///     codec understands), and a 2-byte checksum -- before either message's own fixed body.
    /// </summary>
    public const int Icmpv6HeaderLength = 4;

    /// <summary>
    ///     The length, in bytes, of RFC 4861 Section 4.1's Router Solicitation body -- a single
    ///     4-byte reserved field -- before its own variable-length options area.
    /// </summary>
    public const int RouterSolicitationFixedBodyLength = 4;

    /// <summary>
    ///     The length, in bytes, of RFC 4861 Section 4.2's Router Advertisement body -- Cur Hop
    ///     Limit (1 byte), the M/O flags plus 6 reserved bits (1 byte), Router Lifetime (2 bytes),
    ///     Reachable Time (4 bytes), and Retrans Timer (4 bytes) -- before its own variable-length
    ///     options area.
    /// </summary>
    public const int RouterAdvertisementFixedBodyLength = 12;

    /// <summary>
    ///     The length, in bytes, of one Neighbor Discovery option's own fixed type/length header --
    ///     a 1-byte type and a 1-byte length -- before that option's own value bytes. Unlike
    ///     <see cref="Dhcpv6Constants.OptionHeaderLength" />, RFC 4861 Section 4.6's own "length"
    ///     field counts in units of 8 octets and includes this 2-byte header itself, not merely the
    ///     value bytes that follow it -- a genuinely different framing, not merely a narrower field
    ///     width.
    /// </summary>
    public const int OptionHeaderLength = 2;

    /// <summary>
    ///     The number of bytes one unit of a Neighbor Discovery option's own "length" field
    ///     represents, per RFC 4861 Section 4.6.
    /// </summary>
    public const int OptionLengthUnitBytes = 8;
}
