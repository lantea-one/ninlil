// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the rules a raw (tag and value) DHCP option must meet to be written on the
///     wire. The REST controllers for scope custom options and tag rule options call it when an
///     option is saved, and <c>Dhcpv4OptionOverlayService</c> and <c>Dhcpv6OptionOverlayService</c>
///     call it again before an option reaches a reply, so a stored row that predates a rule costs
///     that one option and not the whole reply.
///     <para>
///         The rules cover the option code (reserved and engine-written codes), the value length,
///         and for a few options the value itself: DHCPv4 TFTP Server Name and Bootfile Name are
///         text, and the DHCPv6 Domain Search List is a list of wire-format domain names.
///     </para>
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
    ///     The DHCPv6 option codes the engine or the relay path writes itself, with the IANA
    ///     DHCPv6 Option Codes registry name. A custom or tag-rule option with one of these codes
    ///     would replace the engine's own option (<c>SetRawOption</c> replaces by code) and break
    ///     the reply: for example, a custom Server Identifier makes every client reject the
    ///     server. Preference (7) is not listed because an operator with two servers may set it.
    /// </summary>
    private static readonly Dictionary<ushort, string> ReservedDhcpv6Codes = new()
    {
        [1] = "Client Identifier",
        [2] = "Server Identifier",
        [3] = "IA_NA",
        [4] = "IA_TA",
        [5] = "IA Address",
        [6] = "Option Request",
        [8] = "Elapsed Time",
        [9] = "Relay Message",
        [11] = "Authentication",
        [13] = "Status Code",
        [14] = "Rapid Commit",
        [18] = "Interface-ID",
        [19] = "Reconfigure Message",
        [20] = "Reconfigure Accept",
        [25] = "IA_PD",
        [26] = "IA Prefix",
        [37] = "Relay Agent Remote-ID",
        [39] = "Client FQDN",
    };

    /// <summary>
    ///     This method checks a DHCPv6 option code and value length. Code 0 is reserved (IANA DHCPv6 option
    ///     codes registry), so are the codes the engine writes itself (see
    ///     <c>ReservedDhcpv6Codes</c>), and the length field is two bytes (RFC 8415 Section 21.1).
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

        if (ReservedDhcpv6Codes.TryGetValue(code, out string reservedName))
        {
            error =
                $"Option code {code} ({reservedName}) is set by the server itself; "
                + "it cannot be a custom option.";
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

    /// <summary>
    ///     This method checks the value of a DHCPv4 option whose content is constrained. Tag 66
    ///     (TFTP Server Name, RFC 2132 Section 9.4) and tag 67 (Bootfile Name, Section 9.5) carry
    ///     text, so every byte must be printable ASCII (0x20 to 0x7E); a binary address stored
    ///     under tag 66 is refused, and option 150 is the binary form. Any other tag passes.
    /// </summary>
    /// <param name="tag">The option tag.</param>
    /// <param name="value">The decoded option value.</param>
    /// <param name="error">Receives a message naming the tag when invalid.</param>
    /// <returns>True when the value may be written.</returns>
    public static bool TryValidateDhcpv4Value(byte tag, ReadOnlySpan<byte> value, out string error)
    {
        error = null;

        if (tag is not (66 or 67))
            return true;

        // The option is text, not an address; the check is printable ASCII because a host name or
        // file name never needs a control or high-bit byte.
        foreach (byte character in value)
            if (character is < 0x20 or > 0x7E)
            {
                error =
                    $"The value of option {tag} is text and must be printable ASCII; use option "
                    + "150 for a binary address.";
                return false;
            }

        return true;
    }

    /// <summary>
    ///     This method checks the value of a DHCPv6 option whose content is constrained. Code 24
    ///     (Domain Search List, RFC 3646 Section 4) must decode with
    ///     <see cref="DhcpDomainNameListCodec.TryDecode" />. Any other code passes.
    /// </summary>
    /// <param name="code">The option code.</param>
    /// <param name="value">The decoded option value.</param>
    /// <param name="error">Receives a message naming the code when invalid.</param>
    /// <returns>True when the value may be written.</returns>
    public static bool TryValidateDhcpv6Value(
        ushort code,
        ReadOnlySpan<byte> value,
        out string error
    )
    {
        error = null;

        if (code != 24)
            return true;

        if (DhcpDomainNameListCodec.TryDecode(value, out _, out string decodeError))
            return true;

        error = $"The value of option 24 (Domain Search List) is not valid: {decodeError}";
        return false;
    }
}
