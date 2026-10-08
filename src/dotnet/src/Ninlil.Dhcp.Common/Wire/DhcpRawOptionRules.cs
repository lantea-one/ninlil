// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the rules a raw (tag and value) DHCP option must meet to be written on the
///     wire. The REST controllers for scope custom options and tag rule options call it when an
///     option is saved, and <c>Dhcpv4OptionOverlayService</c> and <c>Dhcpv6OptionOverlayService</c>
///     call it again before an option reaches a reply, so a stored row that predates a rule costs
///     that one option and not the whole reply.
/// </summary>
public static class DhcpRawOptionRules
{
    /// <summary>
    ///     The largest DHCPv4 option value in bytes. The length field is one byte (RFC 2132
    ///     Section 2).
    /// </summary>
    public const int MaximumDhcpv4ValueLength = byte.MaxValue;

    /// <summary>
    ///     The largest DHCPv6 option value in bytes. The length field is two bytes (RFC 8415
    ///     Section 21.1).
    /// </summary>
    public const int MaximumDhcpv6ValueLength = ushort.MaxValue;

    /// <summary>
    ///     This method checks a DHCPv4 option tag and value length. Tag 0 (Pad) and 255 (End) are
    ///     markers without a length byte (RFC 2132 Section 3.1 and 3.2). Tag 52 (Option Overload),
    ///     53 (DHCP Message Type) and 82 (Relay Agent Information) are written by the server or
    ///     relay agent itself, and a custom value would corrupt the exchange.
    /// </summary>
    /// <param name="tag">The option tag.</param>
    /// <param name="valueLength">The value length in bytes.</param>
    /// <param name="error">Receives a message naming the tag and the limit when invalid.</param>
    /// <returns>True when the option may be written.</returns>
    public static bool TryValidateDhcpv4(byte tag, int valueLength, out string error)
    {
        error = null;

        if (tag is 0 or 255)
        {
            error = $"Option tag {tag} is reserved (0 is Pad and 255 is End); use 1 to 254.";
            return false;
        }

        if (tag is 52 or 53 or 82)
        {
            error =
                $"Option tag {tag} is reserved because the server or relay agent sets it itself; "
                + "it cannot be a custom option.";
            return false;
        }

        if (valueLength > MaximumDhcpv4ValueLength)
        {
            error =
                $"The value of option {tag} is {valueLength} bytes; a DHCPv4 option value is at "
                + $"most {MaximumDhcpv4ValueLength} bytes.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method checks a DHCPv6 option code and value length. Code 0 is reserved (IANA DHCPv6 option
    ///     codes registry) and the length field is two bytes (RFC 8415 Section 21.1).
    /// </summary>
    /// <param name="code">The option code.</param>
    /// <param name="valueLength">The value length in bytes.</param>
    /// <param name="error">Receives a message naming the code and the limit when invalid.</param>
    /// <returns>True when the option may be written.</returns>
    public static bool TryValidateDhcpv6(ushort code, int valueLength, out string error)
    {
        error = null;

        if (code == 0)
        {
            error = "Option code 0 is reserved; use 1 or higher.";
            return false;
        }

        if (valueLength > MaximumDhcpv6ValueLength)
        {
            error =
                $"The value of option {code} is {valueLength} bytes; a DHCPv6 option value is at "
                + $"most {MaximumDhcpv6ValueLength} bytes.";
            return false;
        }

        return true;
    }
}
