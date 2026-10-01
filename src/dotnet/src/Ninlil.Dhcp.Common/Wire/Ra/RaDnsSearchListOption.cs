using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 8106 Section 5.2's DNS Search List (DNSSL) option: one or more
///     search domain suffixes this router advertises, with a lifetime. It is the companion of the
///     Recursive DNS Server option (<see cref="RaMessageOptionExtensions.TryGetRecursiveDnsServers" />,
///     <see cref="RaMessageOptionExtensions.SetRecursiveDnsServers" />) and gives a SLAAC-only
///     host a search list, as the RFC 3397 Domain Search option does for DHCPv4 clients.
///     <para>
///         RFC 8106 Section 5.2 forbids RFC 1035 Section 4.1.4 compression here, since there is no
///         surrounding DNS message. This class therefore has its own compression-free encoder and
///         decoder rather than using <c>Ninlil.Dns.Common</c>'s <c>DnsName</c>, which is built for
///         DNS messages (and <c>Ninlil.Dhcp.Common</c> does not reference that project).
///     </para>
/// </summary>
public class RaDnsSearchListOption
{
    /// <summary>
    ///     This constant contains the length of the fixed part of the value before the domain
    ///     names: a 2-byte reserved field and a 4-byte lifetime (RFC 8106 Section 5.2).
    /// </summary>
    private const int FixedHeaderLength = 6;

    /// <summary>
    ///     This constant contains the longest DNS label, 63 octets (RFC 1035 Section 3.1); the top
    ///     two bits of the length byte are reserved for compression pointers.
    /// </summary>
    private const int MaxLabelLength = 63;

    /// <summary>
    ///     This constant contains the top two bits of a label length byte. With either set, the
    ///     byte starts a compression pointer (RFC 1035 Section 4.1.4) rather than a label, and the
    ///     decoder rejects it because RFC 8106 forbids compression here.
    /// </summary>
    private const byte CompressionPointerMarkerBits = 0b1100_0000;

    /// <summary>
    ///     This property contains how long after this option is received a host may use
    ///     <see cref="DomainNames" /> (RFC 8106 Section 5.2's "Lifetime", 32-bit seconds on the
    ///     wire; a value at or above <see cref="uint.MaxValue" /> seconds is written as infinity).
    /// </summary>
    public TimeSpan Lifetime { get; set; }

    /// <summary>
    ///     This property contains the search domain suffixes, in the order a host should try
    ///     them. RFC 8106 Section 5.2 requires at least one.
    /// </summary>
    public List<string> DomainNames { get; set; } = [];

    /// <summary>
    ///     This method reports whether the encoder can represent a domain name: every label 1 to
    ///     <see cref="MaxLabelLength" /> characters and ASCII only, so an internationalized name
    ///     must be given in its punycode form. <c>RaAdvertisementProvider</c> uses it to skip a
    ///     bad configured entry instead of failing the whole advertisement.
    /// </summary>
    public static bool IsValidDomainName(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return false;

        // A name made only of dots (such as the root ".") encodes as the root label alone:
        // valid, if unusual.
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
    ///     This method decodes a DNS Search List option's value (everything after the type/length
    ///     header). It fails on a compression pointer, a truncated name, or no names at all.
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

        // The names are followed by zero padding up to an 8-octet boundary (RFC 8106 Section
        // 5.2). Once every remaining byte is zero, the rest is padding, not another name.
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
    ///     This method decodes one RFC 1035 Section 3.1 domain name (length-prefixed labels ending
    ///     in a zero-length root label), rejecting compression, and advances
    ///     <paramref name="offset" /> past it.
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

            // A zero-length label is the root label that ends the name.
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
    ///     This method encodes this option's value (everything after the type/length header,
    ///     which <see cref="RaMessageOptionExtensions" /> adds), including the zero padding that
    ///     keeps the option length a multiple of 8 octets as RFC 4861 Section 4.6 requires.
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

            // A name made only of dots encodes as the root label alone.
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

            // The zero-length root label ends this name.
            domainBytes.Add(0);
        }

        // Pad the names to a multiple of 8 octets. The 2-byte type/length header plus the 6-byte
        // fixed part is already 8, so the whole option stays a multiple of 8 (RFC 8106 Section
        // 5.2).
        int paddingNeeded = (8 - (domainBytes.Count % 8)) % 8;
        for (int i = 0; i < paddingNeeded; i++)
            domainBytes.Add(0);

        byte[] result = new byte[FixedHeaderLength + domainBytes.Count];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(2, 4), ToWireSeconds(Lifetime));
        domainBytes.CopyTo(result, FixedHeaderLength);
        return result;
    }

    /// <summary>
    ///     This method converts a lifetime to unsigned 32-bit seconds, writing a value at or above
    ///     <see cref="uint.MaxValue" /> seconds as infinity. A negative value throws.
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
