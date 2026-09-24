using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a single domain name, such as <c>www.example.com</c>, the way DNS
///     itself represents one: as an ordered sequence of labels -- <c>www</c>, <c>example</c>,
///     <c>com</c> -- each stored as its own raw bytes rather than as a single joined string.
///     RFC 1035 §3.1 defines a label as an arbitrary byte sequence, not necessarily printable
///     text, so this class keeps that byte-level fidelity rather than assuming every name is
///     plain ASCII; in practice, the overwhelming majority of real names are ASCII (or the
///     ASCII-only "xn--" punycode form of an internationalized name), but nothing here requires
///     that.
/// </summary>
public sealed class DnsName : IEquatable<DnsName>
{
    /// <summary>
    ///     This constant contains the maximum length, in octets, a single label may occupy on
    ///     the wire, defined by RFC 1035 §3.1.
    /// </summary>
    public const int MaximumLabelLength = 63;

    /// <summary>
    ///     This constant contains the maximum length, in octets, a complete name may occupy on
    ///     the wire (every label's length octet and content, plus the final zero-length root
    ///     label), defined by RFC 1035 §3.1.
    /// </summary>
    public const int MaximumWireLength = 255;

    /// <summary>
    ///     This property contains the root name: the name with no labels at all, written in
    ///     presentation form as a single dot.
    /// </summary>
    public static DnsName Root { get; } = new([]);

    /// <summary>
    ///     This property contains this name's labels, in the same left-to-right order they would
    ///     be written in (for <c>www.example.com</c>: <c>www</c>, then <c>example</c>, then
    ///     <c>com</c>).
    /// </summary>
    public IReadOnlyList<byte[]> Labels { get; }

    /// <summary>
    ///     This property indicates whether this is the root name.
    /// </summary>
    public bool IsRoot => Labels.Count == 0;

    /// <summary>
    ///     This property contains how many octets this name occupies on the wire, uncompressed:
    ///     one length-prefix octet per label plus that label's own octets, plus a final
    ///     zero-length octet marking the root.
    /// </summary>
    public int WireLength => Labels.Sum(label => label.Length + 1) + 1;

    /// <summary>
    ///     This method instantiates a name directly from its labels. Callers outside this class
    ///     should normally use <see cref="Parse" /> instead; this constructor exists for the wire
    ///     reader, which already has each label's raw bytes in hand and has no presentation-format
    ///     string to parse.
    /// </summary>
    /// <param name="labels">This name's labels, in left-to-right order.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when a label exceeds <see cref="MaximumLabelLength" />, or the name as a whole
    ///     would exceed <see cref="MaximumWireLength" /> once encoded.
    /// </exception>
    public DnsName(IReadOnlyList<byte[]> labels)
    {
        foreach (byte[] label in labels)
            if (label.Length > MaximumLabelLength)
                throw new ArgumentException(
                    $"A DNS label may not exceed {MaximumLabelLength} octets.",
                    nameof(labels)
                );

        Labels = labels;

        if (WireLength > MaximumWireLength)
            throw new ArgumentException(
                $"A DNS name may not exceed {MaximumWireLength} octets once encoded.",
                nameof(labels)
            );
    }

    /// <summary>
    ///     This method parses a name's presentation-format representation -- the familiar,
    ///     dot-separated form such as <c>www.example.com</c> -- into a <see cref="DnsName" />.
    ///     A literal dot or backslash inside a label is written escaped, as <c>\.</c> or
    ///     <c>\\</c>; an arbitrary byte value is written as a decimal escape, such as <c>\000</c>.
    /// </summary>
    /// <param name="presentation">The presentation-format name to parse.</param>
    /// <returns>The parsed <see cref="DnsName" />.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="presentation" /> is malformed.</exception>
    public static DnsName Parse(string presentation) =>
        TryParse(presentation, out DnsName name, out string error)
            ? name
            : throw new FormatException(error);

    /// <summary>
    ///     This method attempts to parse a name's presentation-format representation into a
    ///     <see cref="DnsName" />, the same as <see cref="Parse" />, but without throwing when the
    ///     input is malformed.
    /// </summary>
    /// <param name="presentation">The presentation-format name to parse.</param>
    /// <param name="name">
    ///     The parsed <see cref="DnsName" /> if parsing succeeded, otherwise <see langword="null" />.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether parsing succeeded.</returns>
    public static bool TryParse(string presentation, out DnsName name) =>
        TryParse(presentation, out name, out _);

    /// <summary>
    ///     This method does the actual work behind <see cref="Parse" /> and <see cref="TryParse(string,out DnsName)" />.
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
                labels.Add([.. currentLabel]);
                currentLabel.Clear();
            }
            else
            {
                currentLabel.Add((byte)c);
            }
        }

        // A trailing, unescaped dot (the fully-qualified form, "example.com.") simply marks the
        // end of the name; anything else left over is one final label to add.
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
                // Escape the two characters that are otherwise ambiguous in presentation format,
                // and any byte outside the printable ASCII range, as a three-digit decimal value.
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

        // We're done. Every label was followed by a trailing dot above, including the last one,
        // which gives exactly the fully-qualified presentation form callers expect.
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

        // DNS names compare case-insensitively, per RFC 1035 §3.1, but only across the standard
        // ASCII letters; any other byte value must match exactly.
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
    ///     This method upper-cases a single byte if, and only if, it is an ASCII lowercase
    ///     letter, leaving every other byte value (including non-ASCII bytes) untouched.
    /// </summary>
    /// <param name="value">The byte to upper-case.</param>
    /// <returns>The upper-cased byte.</returns>
    private static byte AsciiUpper(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    /// <summary>
    ///     This method encodes this name exactly the way <see cref="Wire.Records.UncompressedDnsNameCodec" />
    ///     does -- one length-prefixed label at a time, followed by the final zero-length root
    ///     label -- but as a public, standalone operation, for callers outside
    ///     <c>Ninlil.Dns.Common</c> that need a name's raw uncompressed bytes without any
    ///     compression support at all: RFC 4034 §5.1.4's DS digest input, for example, which
    ///     hashes a DNSKEY's owner name in exactly this form.
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
    ///     This method returns this name with every ASCII uppercase letter converted to
    ///     lowercase, per RFC 4034 §6.1/§6.2's DNSSEC canonical form -- needed wherever a name
    ///     (an owner name, or an embedded name inside RDATA for the specific record types §6.2
    ///     lists) must be hashed or signed in a case-normalized form, since two zone
    ///     administrators typing <c>WWW.example.com</c> and <c>www.example.com</c> must produce
    ///     an identical signature.
    /// </summary>
    /// <returns>
    ///     This same instance if every label is already lowercase, or a new, lowercased
    ///     <see cref="DnsName" /> otherwise.
    /// </returns>
    public DnsName ToCanonicalLower()
    {
        // Track whether lower-casing ever actually changed a byte while building the lowered
        // copy unconditionally alongside it, rather than checking case first and lowering in a
        // second pass -- one pass over the labels either way, but this avoids a second full scan
        // of every label just to answer "was anything uppercase at all?" up front.
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

        // Return this same instance when nothing actually changed, so a name that's already
        // canonical-form doesn't pay for a redundant allocation on every signing/validation pass.
        return alreadyLower ? this : new DnsName(lowered);
    }

    /// <summary>
    ///     This method lower-cases a single byte if, and only if, it is an ASCII uppercase
    ///     letter, leaving every other byte value (including non-ASCII bytes) untouched.
    /// </summary>
    /// <param name="value">The byte to lower-case.</param>
    /// <returns>The lower-cased byte.</returns>
    private static byte AsciiLower(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
}
