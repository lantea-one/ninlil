using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents a single address-family/prefix item inside an
///     <see cref="AplRecordData" /> record, defined by RFC 3123 §4.
///     <para>
///         Like <see cref="DnsResourceRecord" /> and <see cref="DnsEdnsOption" /> before it, this
///         is a plain class rather than a record: <see cref="AddressFamilyDependentPart" /> is a
///         <see langword="byte[]" />, and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="addressFamily">
///     The IANA address family number the prefix belongs to -- 1 for IPv4, 2 for IPv6.
/// </param>
/// <param name="prefixLength">The prefix length, in bits.</param>
/// <param name="negated">
///     Whether this item excludes the given prefix from the list rather than including it.
/// </param>
/// <param name="addressFamilyDependentPart">
///     The address's significant bytes, with trailing zero bytes trimmed -- for example, a /24
///     IPv4 prefix carries only its first three octets, not all four.
/// </param>
public class AplItem(
    ushort addressFamily,
    byte prefixLength,
    bool negated,
    byte[] addressFamilyDependentPart
) : IEquatable<AplItem>
{
    /// <summary>
    ///     This property contains the IANA address family number the prefix belongs to.
    /// </summary>
    public ushort AddressFamily { get; } = addressFamily;

    /// <summary>
    ///     This property contains the prefix length, in bits.
    /// </summary>
    public byte PrefixLength { get; } = prefixLength;

    /// <summary>
    ///     This property indicates whether this item excludes the given prefix from the list
    ///     rather than including it.
    /// </summary>
    public bool Negated { get; } = negated;

    /// <summary>
    ///     This property contains the address's significant bytes, with trailing zero bytes
    ///     trimmed.
    /// </summary>
    public byte[] AddressFamilyDependentPart { get; } = addressFamilyDependentPart;

    /// <inheritdoc />
    public bool Equals(AplItem other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                AddressFamily == other.AddressFamily
                && PrefixLength == other.PrefixLength
                && Negated == other.Negated
                && AddressFamilyDependentPart
                    .AsSpan()
                    .SequenceEqual(other.AddressFamilyDependentPart)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as AplItem);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(AddressFamily);
        hash.Add(PrefixLength);
        hash.Add(Negated);

        foreach (byte b in AddressFamilyDependentPart)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Apl" /> record,
///     defined by RFC 3123: a list of address prefixes associated with the owner name.
/// </summary>
/// <param name="Items">The address-prefix items making up this record.</param>
public record AplRecordData(IReadOnlyList<AplItem> Items) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="AplRecordData" />.
/// </summary>
public class AplRecordDataCodec : IDnsRecordDataCodec
{
    /// <summary>
    ///     This constant contains the bit RFC 3123 §4 assigns inside the length octet, marking an
    ///     item as negated.
    /// </summary>
    private const byte NegationBit = 0x80;

    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Apl;

    /// <inheritdoc />
    public Type DataType => typeof(AplRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        List<AplItem> items = [];
        int offset = 0;

        // Each item is a variable-length run: a 2-octet address family, a 1-octet prefix
        // length, a 1-octet negation flag + data length, and finally that many octets of
        // address data -- so the loop advances by a different amount on every iteration.
        while (offset < rdata.Length)
        {
            ushort addressFamily = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(offset, 2));
            byte prefixLength = rdata[offset + 2];
            byte negationAndLength = rdata[offset + 3];

            // The top bit of this octet is the negation flag; the remaining 7 bits are the
            // address-data length in octets (never more than 127, well within an IPv6 address).
            bool negated = (negationAndLength & NegationBit) != 0;
            int dataLength = negationAndLength & 0x7F;
            byte[] addressFamilyDependentPart = rdata.Slice(offset + 4, dataLength).ToArray();

            items.Add(
                new AplItem(addressFamily, prefixLength, negated, addressFamilyDependentPart)
            );
            offset += 4 + dataLength;
        }

        return new AplRecordData(items);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        AplRecordData typed = (AplRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        // Re-encode each item back into the same variable-length wire layout Read() decodes.
        foreach (AplItem item in typed.Items)
        {
            Span<byte> familySpan = buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(familySpan, item.AddressFamily);
            buffer.Advance(2);

            buffer.GetSpan(1)[0] = item.PrefixLength;
            buffer.Advance(1);

            // Pack the negation flag back into the top bit alongside the 7-bit data length.
            byte negationAndLength = (byte)(
                (item.Negated ? NegationBit : 0) | (item.AddressFamilyDependentPart.Length & 0x7F)
            );
            buffer.GetSpan(1)[0] = negationAndLength;
            buffer.Advance(1);

            item.AddressFamilyDependentPart.CopyTo(
                buffer.GetSpan(item.AddressFamilyDependentPart.Length)
            );
            buffer.Advance(item.AddressFamilyDependentPart.Length);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
