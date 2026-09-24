// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the fixed sizes and magic values RFC 2131's wire format is built from,
///     shared between <see cref="Dhcpv4MessageReader" /> and <see cref="Dhcpv4MessageWriter" />
///     so the two can never quietly drift out of sync with each other.
/// </summary>
public static class Dhcpv4Constants
{
    /// <summary>
    ///     RFC 2131 Section 3's magic cookie: the four bytes 99.130.83.99, marking the start of
    ///     the options area immediately after the fixed-format header. Every real DHCP message on
    ///     the wire carries exactly these four bytes at this position; a message missing them is
    ///     not a DHCP message at all (at best, a bare, option-less BOOTP message, which this
    ///     project does not attempt to interoperate with).
    /// </summary>
    public static readonly byte[] MagicCookie = [99, 130, 83, 99];

    /// <summary>
    ///     The length, in bytes, of a DHCPv4 message's entire fixed-format header -- everything
    ///     from the "op" field through the end of the "file" field, per RFC 2131 Section 2 --
    ///     before the 4-byte magic cookie and the variable-length options area that follows it.
    /// </summary>
    public const int FixedHeaderLength = 236;

    /// <summary>
    ///     The fixed wire size, in bytes, of the "chaddr" (client hardware address) field.
    /// </summary>
    public const int ClientHardwareAddressFieldLength = 16;

    /// <summary>
    ///     The fixed wire size, in bytes, of the "sname" (server host name) field.
    /// </summary>
    public const int ServerHostNameFieldLength = 64;

    /// <summary>
    ///     The fixed wire size, in bytes, of the "file" (boot file name) field.
    /// </summary>
    public const int BootFileNameFieldLength = 128;

    /// <summary>
    ///     The smallest total message size <see cref="Dhcpv4MessageWriter" /> pads a message up
    ///     to, per RFC 1542 Section 2's own recommendation that a BOOTP/DHCP message occupy at
    ///     least 300 octets, so that a relay agent or client still following that original BOOTP
    ///     minimum-size convention never discards an otherwise-valid, merely-short message.
    /// </summary>
    public const int MinimumMessageLength = 300;
}
