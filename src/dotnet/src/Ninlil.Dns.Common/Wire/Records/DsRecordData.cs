using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data shared by <see cref="DnsResourceRecordTypeEnum.DS" /> and
///     <see cref="DnsResourceRecordTypeEnum.Cds" /> records -- RFC 4034 §5 and RFC 7344
///     respectively define both with an identical RDATA shape, a digest of a child zone's
///     <see cref="DnsKeyRecordData" /> that lets a parent zone vouch for it without needing the
///     full public key. <see cref="DnsResourceRecordTypeEnum.DS" /> is what a parent actually
///     publishes; <see cref="DnsResourceRecordTypeEnum.Cds" /> is the child's own copy of what it
///     wants published, so a parent can pick it up automatically instead of requiring a manual
///     update.
///     <para>
///         This is a plain class rather than a record, since <see cref="Digest" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="keyTag">
///     The keyed <see cref="DnsKeyRecordData" />'s key tag, per RFC 4034 Appendix B -- a short
///     identifier letting a validator narrow down which key among several signed this digest,
///     without needing to try every one.
/// </param>
/// <param name="algorithm">The keyed <see cref="DnsKeyRecordData" />'s own algorithm.</param>
/// <param name="digestType">
///     Which digest algorithm produced <see cref="Digest" />, per IANA's "Delegation Signer
///     (DS) Resource Record (RR) Type Digest Algorithms" registry -- 1 is SHA-1 (RFC 3658), 2 is
///     SHA-256 (RFC 4509), 3 is GOST R 34.11-94 (RFC 5933), 4 is SHA-384 (RFC 6605).
/// </param>
/// <param name="digest">
///     The digest of the keyed <see cref="DnsKeyRecordData" />'s owner name and RDATA, computed
///     per RFC 4034 §5.1.4.
/// </param>
public class DsRecordData(
    ushort keyTag,
    DnssecAlgorithmEnum algorithm,
    byte digestType,
    byte[] digest
) : IDnsRecordData, IEquatable<DsRecordData>
{
    /// <summary>
    ///     This property contains the keyed <see cref="DnsKeyRecordData" />'s key tag.
    /// </summary>
    public ushort KeyTag { get; } = keyTag;

    /// <summary>
    ///     This property contains the keyed <see cref="DnsKeyRecordData" />'s own algorithm.
    /// </summary>
    public DnssecAlgorithmEnum Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains which digest algorithm produced <see cref="Digest" />.
    /// </summary>
    public byte DigestType { get; } = digestType;

    /// <summary>
    ///     This property contains the digest itself.
    /// </summary>
    public byte[] Digest { get; } = digest;

    /// <inheritdoc />
    public bool Equals(DsRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                KeyTag == other.KeyTag
                && Algorithm == other.Algorithm
                && DigestType == other.DigestType
                && Digest.AsSpan().SequenceEqual(other.Digest)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DsRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(KeyTag);
        hash.Add(Algorithm);
        hash.Add(DigestType);

        foreach (byte b in Digest)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="DsRecordData" /> for both
///     <see cref="DnsResourceRecordTypeEnum.DS" /> and <see cref="DnsResourceRecordTypeEnum.Cds" />,
///     since RFC 7344 gives the latter an identical RDATA shape to the former.
/// </summary>
internal static class DsCodec
{
    /// <summary>
    ///     This method decodes a DS-family record's RDATA.
    /// </summary>
    public static DsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort keyTag = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        DnssecAlgorithmEnum algorithm = (DnssecAlgorithmEnum)rdata[2];
        byte digestType = rdata[3];
        byte[] digest = rdata[4..].ToArray();

        return new DsRecordData(keyTag, algorithm, digestType, digest);
    }

    /// <summary>
    ///     This method encodes a DS-family record's RDATA.
    /// </summary>
    public static byte[] Write(DsRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt16BigEndian(header, data.KeyTag);
        header[2] = (byte)data.Algorithm;
        header[3] = data.DigestType;
        buffer.Advance(4);

        data.Digest.CopyTo(buffer.GetSpan(data.Digest.Length));
        buffer.Advance(data.Digest.Length);

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
///     This class reads and writes <see cref="DsRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.DS" /> record type.
/// </summary>
public class DsRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.DS;

    /// <inheritdoc />
    public Type DataType => typeof(DsRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => DsCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => DsCodec.Write((DsRecordData)data);
}

/// <summary>
///     This class reads and writes <see cref="DsRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.Cds" /> record type.
/// </summary>
public class CdsRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Cds;

    /// <inheritdoc />
    public Type DataType => typeof(DsRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => DsCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => DsCodec.Write((DsRecordData)data);
}
