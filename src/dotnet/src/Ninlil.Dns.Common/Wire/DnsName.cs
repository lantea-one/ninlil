// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a domain name, such as <c>www.example.com</c>, the way DNS does: an
///     ordered sequence of labels (<c>www</c>, <c>example</c>, <c>com</c>), each kept as bytes
///     rather than one joined string. RFC 1035 §3.1 allows any bytes in a label, so this class
///     does not assume ASCII, although almost all names are ASCII or the "xn--" punycode form of
///     an internationalized name. Comparison is case-insensitive for ASCII letters only.
///     <para>
///         A name is immutable. It keeps the label list and the label arrays it is given, hands
///         them out through <see cref="Labels" /> and shares them with the names
///         <see cref="Suffix" /> returns, so nothing may write into a label array or change the
///         list after the name is built. The hash code and the presentation text are computed on
///         first use and kept, which is only correct because the labels never change.
///     </para>
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
    ///     This field contains the uncompressed wire length, computed once by the constructor.
    /// </summary>
    private readonly int wireLength;

    /// <summary>
    ///     This field contains the hash code once <see cref="GetHashCode" /> has computed it, and
    ///     zero before. It is one <see langword="int" /> so a concurrent first call can at worst
    ///     compute and store the same value twice; no reader sees a half-written pair of flag and
    ///     value.
    /// </summary>
    private int hashCode;

    /// <summary>
    ///     This field contains the presentation text once <see cref="ToString" /> has built it. A
    ///     reference write is atomic, and every builder produces equal text.
    /// </summary>
    private string text;

    /// <summary>
    ///     This property indicates whether this is the root name.
    /// </summary>
    public bool IsRoot => Labels.Count == 0;

    /// <summary>
    ///     This property contains the uncompressed wire length of this name: one length octet plus
    ///     the content of each label, plus the final root octet.
    /// </summary>
    public int WireLength => wireLength;

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
        int length = 1;

        foreach (byte[] label in labels)
        {
            if (label.Length == 0)
                throw new ArgumentException("A DNS label may not be empty.", nameof(labels));

            if (label.Length > MaximumLabelLength)
                throw new ArgumentException(
                    $"A DNS label may not exceed {MaximumLabelLength} octets.",
                    nameof(labels)
                );

            length += label.Length + 1;
        }

        if (length > MaximumWireLength)
            throw new ArgumentException(
                $"A DNS name may not exceed {MaximumWireLength} octets once encoded.",
                nameof(labels)
            );

        Labels = labels;
        wireLength = length;
    }

    /// <summary>
    ///     This method instantiates a name from labels that the caller has already checked, for
    ///     <see cref="DnsMessageReader" />, which validates each label length and the running
    ///     wire length while it reads, and for <see cref="Suffix" />, whose labels come from a
    ///     valid name. It skips the validation the public constructor repeats.
    /// </summary>
    /// <param name="labels">
    ///     Non-empty labels of at most <see cref="MaximumLabelLength" /> octets. The name keeps
    ///     this array.
    /// </param>
    /// <param name="wireLength">
    ///     The uncompressed wire length of the labels, at most <see cref="MaximumWireLength" />.
    /// </param>
    internal DnsName(byte[][] labels, int wireLength)
    {
        Labels = labels;
        this.wireLength = wireLength;
    }

    /// <summary>
    ///     This method returns this name without its first <paramref name="skipLabels" /> labels:
    ///     for <c>www.example.com</c>, skipping 1 gives <c>example.com</c>. Walking a name's
    ///     parent zones this way costs one small array per step instead of a new validated name.
    ///     The result shares this name's label arrays (no byte is copied) and skips validation,
    ///     because a suffix of a valid name is valid; sharing is safe because a name never changes.
    /// </summary>
    /// <param name="skipLabels">
    ///     How many labels to drop from the left, from 0 (this name) to the label count (the root).
    /// </param>
    /// <returns>
    ///     This same instance when <paramref name="skipLabels" /> is 0, <see cref="Root" /> when it
    ///     equals the label count, and a new name otherwise.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="skipLabels" /> is negative or greater than the label count.
    /// </exception>
    public DnsName Suffix(int skipLabels)
    {
        int count = Labels.Count;

        if ((uint)skipLabels > (uint)count)
            throw new ArgumentOutOfRangeException(
                nameof(skipLabels),
                skipLabels,
                $"A name with {count} labels has no suffix that skips {skipLabels}."
            );

        if (skipLabels == 0)
            return this;

        if (skipLabels == count)
            return Root;

        // Subtract the skipped labels from the known wire length rather than summing what is
        // left; the shorter side is usually the skipped one.
        int suffixWireLength = wireLength;
        for (int i = 0; i < skipLabels; i++)
            suffixWireLength -= Labels[i].Length + 1;

        byte[][] suffixLabels = new byte[count - skipLabels][];
        for (int i = 0; i < suffixLabels.Length; i++)
            suffixLabels[i] = Labels[skipLabels + i];

        return new DnsName(suffixLabels, suffixWireLength);
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

    /// <summary>
    ///     This method returns the presentation form of this name: each label followed by a dot,
    ///     with <c>\.</c> and <c>\\</c> for a literal dot or backslash and a three-digit decimal
    ///     escape for any byte outside printable ASCII. The text is built on the first call and the
    ///     same instance is returned afterwards.
    /// </summary>
    public override string ToString() => text ??= BuildText();

    /// <summary>
    ///     This method builds the presentation text for <see cref="ToString" />.
    /// </summary>
    private string BuildText()
    {
        if (IsRoot)
            return ".";

        // Count the characters first so the string is allocated once at its final size: one for
        // an ordinary byte, two for an escaped dot or backslash, four for a decimal escape, and
        // one for the dot after each label.
        int length = 0;

        foreach (byte[] label in Labels)
        {
            foreach (byte b in label)
                length +=
                    b is (byte)'.' or (byte)'\\' ? 2
                    : b is < 0x20 or > 0x7E ? 4
                    : 1;

            length++;
        }

        return string.Create(
            length,
            Labels,
            static (span, labels) =>
            {
                int position = 0;

                foreach (byte[] label in labels)
                {
                    foreach (byte b in label)
                    {
                        // Escape the two characters that are ambiguous in presentation form, and
                        // write any byte outside printable ASCII as a three-digit decimal escape.
                        if (b is (byte)'.' or (byte)'\\')
                        {
                            span[position++] = '\\';
                            span[position++] = (char)b;
                        }
                        else if (b is < 0x20 or > 0x7E)
                        {
                            span[position++] = '\\';
                            span[position++] = (char)('0' + (b / 100));
                            span[position++] = (char)('0' + (b / 10 % 10));
                            span[position++] = (char)('0' + (b % 10));
                        }
                        else
                        {
                            span[position++] = (char)b;
                        }
                    }

                    // Every label, including the last, is followed by a dot, which gives the
                    // fully-qualified form.
                    span[position++] = '.';
                }
            }
        );
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

    /// <summary>
    ///     This method returns a hash code that agrees with <see cref="Equals(DnsName)" />: it
    ///     hashes the labels' bytes with ASCII letters upper-cased, so names that differ only in
    ///     case hash alike. The value is computed on the first call and kept. It is stable only
    ///     within one process.
    /// </summary>
    public override int GetHashCode()
    {
        int cached = hashCode;

        if (cached != 0)
            return cached;

        HashCode hash = new();

        foreach (byte[] label in Labels)
        foreach (byte b in label)
            hash.Add(AsciiUpper(b));

        // Zero marks "not computed yet", so a name whose hash is zero stores one instead. Equal
        // names still agree because every name goes through this same remapping.
        int computed = hash.ToHashCode();
        cached = computed == 0 ? 1 : computed;
        hashCode = cached;
        return cached;
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
