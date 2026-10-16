using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This static class reads and writes a list of domain names in the wire form of the DHCPv6
///     Domain Search List option (RFC 3646 Section 4, code 24): each name is a run of RFC 1035
///     Section 3.1 length-prefixed labels ending in a zero-length root label, and the names follow
///     each other back to back. Compression is not used (RFC 8415 Section 10 and RFC 3315 Section
///     8 require uncompressed names in DHCPv6), so a compression pointer is a decoding failure.
///     <para>
///         <c>Dhcpv6MessageOptionExtensions</c> uses it for the Domain Search List accessors, and
///         <see cref="DhcpRawOptionRules.TryValidateDhcpv6Value" /> uses it to check a stored
///         option value. The Angular admin console mirrors its rules when it builds the hex.
///     </para>
/// </summary>
public static class DhcpDomainNameListCodec
{
    /// <summary>
    ///     The longest wire form of one name, in octets, including the length bytes and the root
    ///     label (RFC 1035 Section 2.3.4).
    /// </summary>
    public const int MaximumNameOctets = 255;

    /// <summary>
    ///     The longest single label, in octets (RFC 1035 Section 2.3.4).
    /// </summary>
    public const int MaximumLabelOctets = 63;

    /// <summary>
    ///     This method encodes a list of domain names. Each name is trimmed and may end in one dot.
    /// </summary>
    /// <param name="names">One or more domain names.</param>
    /// <param name="value">Receives the wire form on success, otherwise <see langword="null" />.</param>
    /// <param name="error">Receives a message naming the offending name on failure.</param>
    /// <returns>True when every name is valid and the list is not empty.</returns>
    public static bool TryEncode(IReadOnlyList<string> names, out byte[] value, out string error)
    {
        value = null;
        error = null;

        if (names is not { Count: > 0 })
        {
            error = "A domain name list needs at least one name.";
            return false;
        }

        using MemoryStream buffer = new();

        foreach (string rawName in names)
        {
            string name = rawName?.Trim() ?? string.Empty;

            if (name.EndsWith('.'))
                name = name[..^1];

            if (name.Length == 0)
            {
                error = "A domain name cannot be empty.";
                return false;
            }

            long start = buffer.Length;

            foreach (string label in name.Split('.'))
            {
                if (label.Length == 0)
                {
                    error = $"The domain name \"{rawName}\" has an empty label.";
                    return false;
                }

                if (label.Length > MaximumLabelOctets)
                {
                    error =
                        $"A label of the domain name \"{rawName}\" is longer than the "
                        + $"{MaximumLabelOctets} octets a DNS label can hold.";
                    return false;
                }

                // Printable ASCII only: the label bytes are written as-is, and a non-ASCII or
                // control character would not survive the client's resolver.
                foreach (char character in label)
                    if (character is < ' ' or > '~')
                    {
                        error =
                            $"The domain name \"{rawName}\" contains a character that is not "
                            + "printable ASCII.";
                        return false;
                    }

                buffer.WriteByte((byte)label.Length);
                buffer.Write(Encoding.ASCII.GetBytes(label));
            }

            buffer.WriteByte(0);

            if (buffer.Length - start > MaximumNameOctets)
            {
                error =
                    $"The domain name \"{rawName}\" is longer than the {MaximumNameOctets} "
                    + "octets a DNS name can take on the wire.";
                return false;
            }
        }

        value = buffer.ToArray();
        return true;
    }

    /// <summary>
    ///     This method decodes a list of domain names. It fails on a compression pointer or a
    ///     reserved label type (a length byte with either top bit set), a label that runs past the
    ///     end, a name without a root label, a name over 255 octets, a non-printable label byte,
    ///     an empty value and any bytes after the last root label.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <param name="names">Receives the names, dot-joined and without a trailing dot.</param>
    /// <param name="error">Receives a message describing the first problem on failure.</param>
    /// <returns>True when the whole value is a list of valid names.</returns>
    public static bool TryDecode(
        ReadOnlySpan<byte> value,
        out IReadOnlyList<string> names,
        out string error
    )
    {
        names = null;
        error = null;

        if (value.IsEmpty)
        {
            error = "A domain name list needs at least one name.";
            return false;
        }

        List<string> decoded = [];
        int offset = 0;

        while (offset < value.Length)
        {
            StringBuilder name = new();
            int nameStart = offset;
            bool sawRootLabel = false;

            while (offset < value.Length)
            {
                byte labelLength = value[offset];
                offset++;

                // Either of the top two bits set marks an RFC 1035 Section 4.1.4 compression
                // pointer or a reserved label type. DHCPv6 names are never compressed.
                if ((labelLength & 0xC0) != 0)
                {
                    error =
                        "The domain name list uses a compressed or reserved label; names must be "
                        + "uncompressed (RFC 3646 Section 4).";
                    return false;
                }

                if (labelLength == 0)
                {
                    sawRootLabel = true;
                    break;
                }

                if (offset + labelLength > value.Length)
                {
                    error = "A label runs past the end of the domain name list.";
                    return false;
                }

                if (name.Length > 0)
                    name.Append('.');

                foreach (byte labelByte in value.Slice(offset, labelLength))
                {
                    if (labelByte is < 0x20 or > 0x7E)
                    {
                        error = "A label of the domain name list is not printable ASCII.";
                        return false;
                    }

                    name.Append((char)labelByte);
                }

                offset += labelLength;
            }

            if (!sawRootLabel)
            {
                error = "A domain name in the list has no terminating zero-length root label.";
                return false;
            }

            if (offset - nameStart > MaximumNameOctets)
            {
                error = $"A domain name in the list is longer than {MaximumNameOctets} octets.";
                return false;
            }

            decoded.Add(name.ToString());
        }

        names = decoded;
        return true;
    }
}
