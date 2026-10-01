// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the fixed sizes and magic values of the RFC 2131 wire format, shared by
///     <see cref="Dhcpv4MessageReader" /> and <see cref="Dhcpv4MessageWriter" /> so the two
///     cannot drift apart.
/// </summary>
public static class Dhcpv4Constants
{
    /// <summary>
    ///     RFC 2131 Section 3's magic cookie: the four bytes 99.130.83.99, marking the start of
    ///     the options area right after the fixed header. A message without them is not a DHCP
    ///     message (at best a plain BOOTP message, which is not supported).
    /// </summary>
    public static readonly byte[] MagicCookie = [99, 130, 83, 99];

    /// <summary>
    ///     The length, in bytes, of the fixed header, from the "op" field to the end of the
    ///     "file" field (RFC 2131 Section 2). The magic cookie and the options follow it.
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
    ///     The smallest message size <see cref="Dhcpv4MessageWriter" /> pads to. RFC 1542 Section
    ///     2 recommends at least 300 octets, so a relay agent or client that still applies the
    ///     BOOTP minimum does not discard a short message.
    /// </summary>
    public const int MinimumMessageLength = 300;
}
