using System.Globalization;

// Define our namespace.
namespace Ninlil.Dns.Common.Net;

/// <summary>
///     This static class gives every part of Ninlil one normal form of a client identifier: a MAC
///     address, a DHCPv4 Client-identifier (RFC 2132 Section 9.14) or a DUID (RFC 8415
///     Section 11). All three are opaque byte strings to the matchers, so one rule serves them.
///     <para>
///         The normal form is lowercase hex, two digits per byte, joined by colons
///         (<c>aa:bb:cc:dd:ee:ff</c>). It is the form the DHCP engine writes into lease rows and
///         looks reservations up by, so reservations, clients, client addresses and static
///         address mappings store this form on write and matching is an exact string comparison.
///         <see cref="TryNormalize" /> accepts the spellings operators copy from other tools:
///         colon or hyphen separated groups of one or two digits, dotted groups of four digits
///         (<c>aabb.ccdd.eeff</c>) and unseparated hex.
///     </para>
/// </summary>
public static class ClientIdentifierFormat
{
    /// <summary>
    ///     The largest identifier, in bytes, whose normal form fits the 256-character
    ///     identifier columns (85 bytes are 254 characters).
    /// </summary>
    public const int MaximumBytes = 85;

    /// <summary>
    ///     The text the web API returns in a 400 when <see cref="TryNormalize" /> refuses a
    ///     <c>clientIdentifier</c>. It names the accepted spellings and the size limit.
    /// </summary>
    public const string AcceptedFormsMessage =
        "'clientIdentifier' must be a MAC address, Client-identifier or DUID in hex: bytes "
        + "separated by ':' or '-' (aa:bb:cc:dd:ee:ff), dotted groups of four digits "
        + "(aabb.ccdd.eeff) or unseparated hex (aabbccddeeff), at most 85 bytes. It is stored "
        + "as aa:bb:cc:dd:ee:ff.";

    /// <summary>
    ///     This method reads an identifier in any accepted spelling and returns its normal form.
    ///     Surrounding white space is ignored. Mixed separators, odd-length unseparated hex,
    ///     <c>0x</c> prefixes, empty groups, empty input and more than <see cref="MaximumBytes" />
    ///     bytes are refused.
    /// </summary>
    /// <param name="value">The identifier text to read, or <see langword="null" />.</param>
    /// <param name="normalized">The normal form, or <see langword="null" /> when refused.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is an accepted identifier.</returns>
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        byte[] bytes;

        if (text.Contains('.'))
        {
            if (!TryReadGroups(text.Split('.'), 4, 4, out bytes))
                return false;
        }
        else if (text.Contains(':') || text.Contains('-'))
        {
            if (text.Contains(':') && text.Contains('-'))
                return false;

            if (!TryReadGroups(text.Split(text.Contains(':') ? ':' : '-'), 1, 2, out bytes))
                return false;
        }
        else
        {
            if (text.Length % 2 != 0)
                return false;

            string[] pairs = new string[text.Length / 2];
            for (int i = 0; i < pairs.Length; i++)
                pairs[i] = text.Substring(i * 2, 2);

            if (!TryReadGroups(pairs, 2, 2, out bytes))
                return false;
        }

        if (bytes.Length is 0 or > MaximumBytes)
            return false;

        normalized = Format(bytes);
        return true;
    }

    /// <summary>
    ///     This method formats identifier bytes in the normal form.
    /// </summary>
    /// <param name="identifier">The raw identifier bytes.</param>
    /// <returns>The lowercase, colon-separated hex string.</returns>
    public static string Format(ReadOnlySpan<byte> identifier)
    {
        System.Text.StringBuilder builder = new(identifier.Length * 3);
        for (int i = 0; i < identifier.Length; i++)
        {
            if (i > 0)
                builder.Append(':');
            builder.Append(identifier[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This method reports whether two identifier texts name the same identifier. Both are
    ///     normalised first; a text that is not an identifier never matches anything.
    /// </summary>
    /// <param name="first">The first identifier text.</param>
    /// <param name="second">The second identifier text.</param>
    /// <returns><see langword="true" /> when both are identifiers with the same bytes.</returns>
    public static bool AreEqual(string first, string second) =>
        TryNormalize(first, out string a)
        && TryNormalize(second, out string b)
        && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    ///     This method reads groups of hex digits (each between the given lengths) into bytes.
    ///     Four-digit groups yield two bytes, so a group length of four requires exactly four.
    /// </summary>
    private static bool TryReadGroups(
        string[] groups,
        int minimumDigits,
        int maximumDigits,
        out byte[] bytes
    )
    {
        List<byte> result = new(groups.Length * 2);
        bytes = null;

        foreach (string group in groups)
        {
            if (group.Length < minimumDigits || group.Length > maximumDigits)
                return false;

            foreach (char c in group)
                if (!Uri.IsHexDigit(c))
                    return false;

            if (group.Length == 4)
            {
                result.Add(byte.Parse(group.AsSpan(0, 2), NumberStyles.HexNumber));
                result.Add(byte.Parse(group.AsSpan(2, 2), NumberStyles.HexNumber));
            }
            else
                result.Add(byte.Parse(group, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        bytes = [.. result];
        return true;
    }
}
