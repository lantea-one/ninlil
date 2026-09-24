using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 8106 Section 5.2's DNS Search List (DNSSL) option: one or more
///     DNS search domain suffixes this router advertises, plus how long a host should trust them
///     -- the counterpart to that same RFC's Recursive DNS Server option
///     (<see cref="RaMessageOptionExtensions.TryGetRecursiveDnsServers" />/
///     <see cref="RaMessageOptionExtensions.SetRecursiveDnsServers" />), giving a SLAAC-only host
///     (no DHCPv6 at all) an unqualified-name search list the same way DHCPv4's own Domain Search
///     option (RFC 3397) does for a DHCPv4 client.
///     <para>
///         Unlike every other value this project's DNS wire format ever writes, a domain name
///         inside this option is <b>never</b> allowed to use RFC 1035 Section 4.1.4 message
///         compression -- RFC 8106 Section 5.2 explicitly forbids a compression pointer here, since
///         this option has no surrounding DNS message to compress against in the first place. This
///         class therefore implements its own small, dedicated, compression-free domain-name
///         encoder/decoder rather than reusing <c>Ninlil.Dns.Common</c>'s own <c>DnsName</c>
///         encoder, which assumes an ordinary DNS message's own compression-eligible framing.
///     </para>
/// </summary>
public class RaDnsSearchListOption
{
    /// <summary>
    ///     This constant contains the fixed length, in bytes, of this option's own value that
    ///     precedes its variable-length domain-name portion -- a 2-byte reserved field and a
    ///     4-byte lifetime field, per RFC 8106 Section 5.2.
    /// </summary>
    private const int FixedHeaderLength = 6;

    /// <summary>
    ///     This constant contains the longest a single DNS label may be, per RFC 1035 Section
    ///     3.1's own 6-bit length-prefix ceiling (63, since the top two bits of that prefix byte
    ///     are reserved by RFC 1035 Section 4.1.4 to mark a compression pointer -- a value this
    ///     option's own decoder must reject outright, per this class's own doc comment).
    /// </summary>
    private const int MaxLabelLength = 63;

    /// <summary>
    ///     This constant contains RFC 1035 Section 4.1.4's own compression-pointer marker bits --
    ///     a label length byte with either of its top two bits set is not an ordinary label length
    ///     at all, but the first byte of a compression pointer. RFC 8106 Section 5.2 forbids
    ///     compression inside this option, so this decoder rejects any such byte outright rather
    ///     than attempting to follow it.
    /// </summary>
    private const byte CompressionPointerMarkerBits = 0b1100_0000;

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, a host should
    ///     consider <see cref="DomainNames" /> valid for DNS search-list use before re-querying for
    ///     a fresh Router Advertisement -- RFC 8106 Section 5.2's own "Lifetime" field, a 32-bit
    ///     number of seconds on the wire.
    /// </summary>
    public TimeSpan Lifetime { get; set; }

    /// <summary>
    ///     This property contains every DNS search domain suffix this option advertises, in the
    ///     order a host should try them -- RFC 8106 Section 5.2 requires at least one.
    /// </summary>
    public List<string> DomainNames { get; set; } = [];

    /// <summary>
    ///     This method reports whether a domain name is one this option's own encoder can actually
    ///     represent on the wire -- every label between 1 and <see cref="MaxLabelLength" /> octets,
    ///     ASCII only (a real DNS wire label is octet-based, but this project's own configuration
    ///     surface accepts a plain string; an internationalized suffix must already be supplied in
    ///     its own ASCII-compatible/punycode form, exactly as every other domain name this project
    ///     accepts from configuration or the REST API is expected to be). Used by
    ///     <c>RaAdvertisementProvider</c> to silently skip an unusable configured entry rather than
    ///     let one bad value crash the whole advertisement -- the same "skip what doesn't parse,
    ///     keep going" precedent its own prefix/DNS-server handling already establishes.
    /// </summary>
    public static bool IsValidDomainName(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return false;

        // A domain name consisting of nothing but a trailing dot (or the bare root "." itself)
        // encodes as the single zero-length root label -- valid, if unusual to configure.
        string trimmed = domainName.Trim('.');
        if (trimmed.Length == 0)
            return true;

        foreach (string label in trimmed.Split('.'))
        {
            if (label.Length is 0 or > MaxLabelLength)
                return false;

            foreach (char character in label)
                if (character > 127)
                    return false;
        }

        return true;
    }

    /// <summary>
    ///     This method decodes a DNS Search List option's own value bytes (everything after its
    ///     own type/length header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out RaDnsSearchListOption option,
        out string failureReason
    )
    {
        option = null;

        if (data.Length < FixedHeaderLength)
        {
            failureReason =
                $"A DNS Search List option's own value must be at least {FixedHeaderLength} bytes long.";
            return false;
        }

        uint lifetimeSeconds = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2, 4));

        List<string> domainNames = [];
        int offset = FixedHeaderLength;

        // A real DNS Search List option's own domain-name portion is padded with trailing zero
        // octets so the whole option's length lands on an 8-octet boundary (RFC 8106 Section
        // 5.2's own last paragraph) -- once every remaining byte from here to the end of the
        // option's value is zero, what remains is pure padding, not one more (empty) domain name.
        while (offset < data.Length && data[offset..].IndexOfAnyExcept((byte)0) >= 0)
        {
            if (!TryDecodeOneDomainName(data, ref offset, out string domainName, out failureReason))
                return false;

            domainNames.Add(domainName);
        }

        if (domainNames.Count == 0)
        {
            failureReason = "A DNS Search List option must carry at least one domain name.";
            return false;
        }

        option = new RaDnsSearchListOption
        {
            Lifetime = TimeSpan.FromSeconds(lifetimeSeconds),
            DomainNames = domainNames,
        };
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method decodes exactly one RFC 1035 Section 3.1-style length-prefixed domain name
    ///     -- a sequence of length-prefixed labels terminated by a zero-length root label -- with
    ///     no message compression ever honored, advancing <paramref name="offset" /> past it.
    /// </summary>
    private static bool TryDecodeOneDomainName(
        ReadOnlySpan<byte> data,
        ref int offset,
        out string domainName,
        out string failureReason
    )
    {
        List<string> labels = [];

        while (true)
        {
            if (offset >= data.Length)
            {
                domainName = null;
                failureReason = "A domain name inside a DNS Search List option is truncated.";
                return false;
            }

            byte labelLength = data[offset];

            if ((labelLength & CompressionPointerMarkerBits) != 0)
            {
                domainName = null;
                failureReason =
                    "A DNS Search List option's own domain name may never use message compression.";
                return false;
            }

            offset++;

            // A zero-length label is the root label -- the terminator for this one domain name,
            // not a label to add to it.
            if (labelLength == 0)
                break;

            if (offset + labelLength > data.Length)
            {
                domainName = null;
                failureReason = "A domain name label inside a DNS Search List option is truncated.";
                return false;
            }

            labels.Add(Encoding.ASCII.GetString(data.Slice(offset, labelLength)));
            offset += labelLength;
        }

        domainName = string.Join('.', labels);
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method encodes this option's own value bytes (everything after its own
    ///     type/length header, which the caller -- <see cref="RaMessageOptionExtensions" /> --
    ///     adds separately), including the trailing zero-padding that keeps the option's own total
    ///     length an 8-octet-unit multiple as RFC 4861 Section 4.6's own general option framing
    ///     requires.
    /// </summary>
    internal byte[] Encode()
    {
        if (DomainNames is not { Count: > 0 })
            throw new ArgumentException(
                "A DNS Search List option must carry at least one domain name.",
                nameof(DomainNames)
            );

        List<byte> domainBytes = [];

        foreach (string domainName in DomainNames)
        {
            string trimmed = (domainName ?? string.Empty).Trim('.');

            // A trimmed-to-empty domain name (the bare root, or a lone trailing dot) encodes as
            // just the zero-length root label with no other labels ahead of it.
            if (trimmed.Length > 0)
                foreach (string label in trimmed.Split('.'))
                {
                    byte[] labelBytes = Encoding.ASCII.GetBytes(label);

                    if (labelBytes.Length is 0 or > MaxLabelLength)
                        throw new ArgumentException(
                            $"'{label}' is not a valid DNS label -- it must be between 1 and "
                                + $"{MaxLabelLength} octets long.",
                            nameof(DomainNames)
                        );

                    domainBytes.Add((byte)labelBytes.Length);
                    domainBytes.AddRange(labelBytes);
                }

            // The zero-length root label terminates this one domain name.
            domainBytes.Add(0);
        }

        // Pad the domain-name portion itself out to an 8-octet-unit multiple: combined with this
        // option's own fixed 6-byte reserved/lifetime header (6 + 2-byte type/length header = 8,
        // itself already a multiple of 8), that keeps the option's whole total length a multiple
        // of 8 too, exactly as RFC 8106 Section 5.2's own closing paragraph requires.
        int paddingNeeded = (8 - (domainBytes.Count % 8)) % 8;
        for (int i = 0; i < paddingNeeded; i++)
            domainBytes.Add(0);

        byte[] result = new byte[FixedHeaderLength + domainBytes.Count];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(2, 4), ToWireSeconds(Lifetime));
        domainBytes.CopyTo(result, FixedHeaderLength);
        return result;
    }

    /// <summary>
    ///     This method converts a lifetime to the unsigned 32-bit number of seconds RFC 8106
    ///     writes it as.
    /// </summary>
    private static uint ToWireSeconds(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "A DNS Search List lifetime cannot be negative."
            );

        double totalSeconds = lifetime.TotalSeconds;
        return totalSeconds >= uint.MaxValue ? uint.MaxValue : (uint)totalSeconds;
    }
}
