using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a domain name, such as <c>www.example.com</c>, the way DNS does: an
///     ordered sequence of labels (<c>www</c>, <c>example</c>, <c>com</c>), each kept as bytes
///     rather than one joined string. RFC 1035 §3.1 allows any bytes in a label, so this class
///     does not assume ASCII, although almost all names are ASCII or the "xn--" punycode form of
///     an internationalized name. Comparison is case-insensitive for ASCII letters only.
/// </summary>
public sealed class DnsName : IEquatable<DnsName>
{
    /// <summary>
    ///     This constant contains the maximum length, in octets, a single label may occupy on
    ///     the wire, defined by RFC 1035 §3.1.
    /// </summary>
    public const int MaximumLabelLength = 63;

    /// <summary>
    ///     This constant contains the maximum length, in octets, of a complete name on the wire
    ///     (each label's length octet and content, plus the final root label), defined by RFC 1035
    ///     §3.1.
    /// </summary>
    public const int MaximumWireLength = 255;

    /// <summary>
    ///     This property contains the root name: the name with no labels, written as a single dot.
    /// </summary>
    public static DnsName Root { get; } = new([]);

    /// <summary>
    ///     This property contains this name's labels in left-to-right order (for
    ///     <c>www.example.com</c>: <c>www</c>, <c>example</c>, <c>com</c>).
    /// </summary>
    public IReadOnlyList<byte[]> Labels { get; }

    /// <summary>
    ///     This property indicates whether this is the root name.
    /// </summary>
    public bool IsRoot => Labels.Count == 0;

    /// <summary>
    ///     This property contains the uncompressed wire length of this name: one length octet plus
    ///     the content of each label, plus the final root octet.
    /// </summary>
    public int WireLength => Labels.Sum(label => label.Length + 1) + 1;

    /// <summary>
    ///     This method instantiates a name from its labels. Most callers should use
    ///     <see cref="Parse" />; this constructor is for the wire reader, which already has the
    ///     label bytes and no presentation string.
    /// </summary>
    /// <param name="labels">This name's labels, in left-to-right order.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when a label is empty or exceeds <see cref="MaximumLabelLength" />, or the name as a whole
    ///     would exceed <see cref="MaximumWireLength" /> once encoded.
    /// </exception>
    public DnsName(IReadOnlyList<byte[]> labels)
    {
        foreach (byte[] label in labels)
        {
            if (label.Length == 0)
                throw new ArgumentException("A DNS label may not be empty.", nameof(labels));

            if (label.Length > MaximumLabelLength)
                throw new ArgumentException(
                    $"A DNS label may not exceed {MaximumLabelLength} octets.",
                    nameof(labels)
                );
        }

        Labels = labels;

        if (WireLength > MaximumWireLength)
            throw new ArgumentException(
                $"A DNS name may not exceed {MaximumWireLength} octets once encoded.",
                nameof(labels)
            );
    }

    /// <summary>
    ///     This method parses a name's presentation form, the dot-separated form such as
    ///     <c>www.example.com</c>, into a <see cref="DnsName" />. A literal dot or backslash inside a
    ///     label is escaped as <c>\.</c> or <c>\\</c>; any byte value can be written as a decimal
    ///     escape such as <c>\000</c>.
    /// </summary>
    /// <param name="presentation">The presentation-format name to parse.</param>
    /// <returns>The parsed <see cref="DnsName" />.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="presentation" /> is malformed.</exception>
    public static DnsName Parse(string presentation) =>
        TryParse(presentation, out DnsName name, out string error)
            ? name
            : throw new FormatException(error);

    /// <summary>
    ///     This method parses a name's presentation form like <see cref="Parse" />, but returns
    ///     <see langword="false" /> instead of throwing when the input is malformed.
    /// </summary>
    /// <param name="presentation">The presentation-format name to parse.</param>
    /// <param name="name">
    ///     The parsed <see cref="DnsName" /> if parsing succeeded, otherwise <see langword="null" />.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether parsing succeeded.</returns>
    public static bool TryParse(string presentation, out DnsName name) =>
        TryParse(presentation, out name, out _);

    /// <summary>
    ///     This method does the work behind <see cref="Parse" /> and <see cref="TryParse(string,out DnsName)" />.
    /// </summary>
    /// <param name="presentation">The presentation-format name to parse.</param>
    /// <param name="name">
    ///     The parsed <see cref="DnsName" /> if parsing succeeded, otherwise <see langword="null" />.
    /// </param>
    /// <param name="error">
    ///     A human-readable description of why parsing failed, if it did, otherwise <see langword="null" />.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether parsing succeeded.</returns>
    public static bool TryParse(string presentation, out DnsName name, out string error)
    {
        name = null;
        error = null;

        // The root name is written as a single dot, or an empty string.
        if (string.IsNullOrEmpty(presentation) || presentation == ".")
        {
            name = Root;
            return true;
        }

        List<byte[]> labels = [];
        List<byte> currentLabel = [];

        for (int i = 0; i < presentation.Length; i++)
        {
            char c = presentation[i];

            if (c == '\\')
            {
                // An escape sequence is either a three-digit decimal byte value ("\000"-"\255")
                // or a single literal character ("\." or "\\").
                if (i + 3 < presentation.Length && char.IsDigit(presentation[i + 1]))
                {
                    if (
                        !int.TryParse(presentation.AsSpan(i + 1, 3), out int decimalValue)
                        || decimalValue is < 0 or > 255
                    )
                    {
                        error =
                            $"'{presentation}' contains an invalid decimal escape sequence at position {i}.";
                        return false;
                    }

                    currentLabel.Add((byte)decimalValue);
                    i += 3;
                }
                else if (i + 1 < presentation.Length)
                {
                    if (presentation[i + 1] > 0x7F)
                    {
                        error = NonAsciiError(presentation, i + 1);
                        return false;
                    }

                    currentLabel.Add((byte)presentation[i + 1]);
                    i++;
                }
                else
                {
                    error = $"'{presentation}' ends with a dangling escape character.";
                    return false;
                }
            }
            else if (c == '.')
            {
                if (currentLabel.Count == 0)
                {
                    error = $"'{presentation}' contains an empty label at position {i}.";
                    return false;
                }

                labels.Add([.. currentLabel]);
                currentLabel.Clear();
            }
            else
            {
                // A char above 0x7F would be truncated to a byte; the caller must convert
                // Unicode names to the xn-- form first.
                if (c > 0x7F)
                {
                    error = NonAsciiError(presentation, i);
                    return false;
                }

                currentLabel.Add((byte)c);
            }
        }

        // A trailing unescaped dot (the fully-qualified form, "example.com.") just ends the
        // name; anything left over is one final label.
        if (currentLabel.Count > 0)
            labels.Add([.. currentLabel]);

        try
        {
            name = new DnsName(labels);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    ///     This method builds the error for a non-ASCII character in a presentation name.
    /// </summary>
    private static string NonAsciiError(string presentation, int position) =>
        $"'{presentation}' contains a non-ASCII character at position {position}; use the xn-- form or a \\DDD escape.";

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsRoot)
            return ".";

        StringBuilder builder = new();

        foreach (byte[] label in Labels)
        {
            foreach (byte b in label)
            {
                // Escape the two characters that are ambiguous in presentation form, and write any
                // byte outside printable ASCII as a three-digit decimal escape.
                if (b is (byte)'.' or (byte)'\\')
                {
                    builder.Append('\\').Append((char)b);
                }
                else if (b is < 0x20 or > 0x7E)
                {
                    builder.Append('\\').Append(b.ToString("D3"));
                }
                else
                {
                    builder.Append((char)b);
                }
            }

            builder.Append('.');
        }

        // Every label, including the last, is followed by a dot, which gives the
        // fully-qualified form.
        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(DnsName other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (Labels.Count != other.Labels.Count)
            return false;

        // DNS names compare case-insensitively (RFC 1035 §3.1), but only for ASCII letters;
        // any other byte must match exactly.
        for (int i = 0; i < Labels.Count; i++)
        {
            byte[] left = Labels[i];
            byte[] right = other.Labels[i];

            if (left.Length != right.Length)
                return false;

            for (int j = 0; j < left.Length; j++)
                if (AsciiUpper(left[j]) != AsciiUpper(right[j]))
                    return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DnsName);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();

        foreach (byte[] label in Labels)
        foreach (byte b in label)
            hash.Add(AsciiUpper(b));

        return hash.ToHashCode();
    }

    /// <summary>
    ///     This operator compares two names for case-insensitive equality.
    /// </summary>
    public static bool operator ==(DnsName left, DnsName right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    ///     This operator compares two names for case-insensitive inequality.
    /// </summary>
    public static bool operator !=(DnsName left, DnsName right) => !(left == right);

    /// <summary>
    ///     This method upper-cases a byte only if it is an ASCII lowercase letter.
    /// </summary>
    /// <param name="value">The byte to upper-case.</param>
    /// <returns>The upper-cased byte.</returns>
    private static byte AsciiUpper(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    /// <summary>
    ///     This method encodes this name as <see cref="Wire.Records.UncompressedDnsNameCodec" /> does
    ///     (length-prefixed labels followed by the root label), as a public operation for callers
    ///     outside <c>Ninlil.Dns.Common</c>. The DS digest input of RFC 4034 §5.1.4, for example,
    ///     hashes a DNSKEY owner name in this form.
    /// </summary>
    /// <returns>This name's uncompressed wire-format bytes.</returns>
    public byte[] ToUncompressedWireBytes()
    {
        byte[] bytes = new byte[WireLength];
        int offset = 0;

        foreach (byte[] label in Labels)
        {
            bytes[offset] = (byte)label.Length;
            label.CopyTo(bytes, offset + 1);
            offset += 1 + label.Length;
        }

        bytes[offset] = 0;

        return bytes;
    }

    /// <summary>
    ///     This method returns this name with the labels of <paramref name="relativeName" /> in
    ///     front of it, for example <c>"_acme-challenge"</c> before <c>example.com</c>. Building a
    ///     child name this way, rather than by joining presentation text, keeps the root (".")
    ///     working: <c>"*." + "."</c> would be <c>"*.."</c>, which has an empty label.
    /// </summary>
    /// <param name="relativeName">
    ///     One or more labels in presentation format, without a trailing dot.
    /// </param>
    /// <returns>The longer name.</returns>
    /// <exception cref="FormatException">
    ///     <paramref name="relativeName" /> is not a valid name, or the result is longer than 255
    ///     octets.
    /// </exception>
    public DnsName PrependLabels(string relativeName)
    {
        DnsName relative = Parse(relativeName);

        // The constructor enforces the 255-octet limit with an ArgumentException; callers of a
        // parse-style method expect a FormatException.
        try
        {
            return new DnsName([.. relative.Labels, .. Labels]);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException(
                $"'{relativeName}' in front of '{this}' is not a valid name: {exception.Message}",
                exception
            );
        }
    }

    /// <summary>
    ///     This method returns this name with ASCII uppercase letters converted to lowercase, the
    ///     DNSSEC canonical form of RFC 4034 §6.1 and §6.2. Owner names, and names inside the RDATA
    ///     of the types §6.2 lists, are hashed and signed in this form so that
    ///     <c>WWW.example.com</c> and <c>www.example.com</c> produce the same signature.
    /// </summary>
    /// <returns>
    ///     This same instance if every label is already lowercase, or a new, lowercased
    ///     <see cref="DnsName" /> otherwise.
    /// </returns>
    public DnsName ToCanonicalLower()
    {
        // Build the lowered copy and track whether any byte changed in the same pass.
        bool alreadyLower = true;
        List<byte[]> lowered = new(Labels.Count);

        foreach (byte[] label in Labels)
        {
            byte[] loweredLabel = new byte[label.Length];

            for (int i = 0; i < label.Length; i++)
            {
                loweredLabel[i] = AsciiLower(label[i]);

                if (loweredLabel[i] != label[i])
                    alreadyLower = false;
            }

            lowered.Add(loweredLabel);
        }

        // Return this instance when nothing changed, so an already-canonical name does not
        // allocate on every signing or validation pass.
        return alreadyLower ? this : new DnsName(lowered);
    }

    /// <summary>
    ///     This method lower-cases a byte only if it is an ASCII uppercase letter.
    /// </summary>
    /// <param name="value">The byte to lower-case.</param>
    /// <returns>The lower-cased byte.</returns>
    private static byte AsciiLower(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
}
