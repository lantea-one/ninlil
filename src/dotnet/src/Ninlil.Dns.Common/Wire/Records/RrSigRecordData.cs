using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.RrSig" />
///     record, defined by RFC 4034 §3: the signature over one complete RRset that lets a
///     validating resolver trust it came unmodified from the zone's owner.
///     <para>
///         This is a plain class rather than a record, since <see cref="Signature" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="typeCovered">Which RRset (by type) this signature covers.</param>
/// <param name="algorithm">The signing algorithm used to produce <see cref="Signature" />.</param>
/// <param name="labels">
///     The number of labels in the signed RRset's owner name, not counting the root label or a
///     leading wildcard label (RFC 4034 §3.1.3). It lets a validator tell that an answer was
///     synthesized from a wildcard, and reject one that claims more labels than the signature
///     allows.
/// </param>
/// <param name="originalTimeToLiveSeconds">
///     The signed RRset's TTL in the authoritative zone (RFC 4034 §3.1.4). It is carried
///     separately from the served TTL, which caches count down, because the signature covers
///     this original value and would not verify against a lower one.
/// </param>
/// <param name="signatureExpiration">
///     The point after which this signature is no longer valid, as a 32-bit count of seconds
///     since the Unix epoch, per RFC 4034 §3.1.5.
/// </param>
/// <param name="signatureInception">
///     The point before which this signature is not yet valid, in the same form as
///     <see cref="SignatureExpiration" />.
/// </param>
/// <param name="keyTag">
///     The key tag of the signing <see cref="DnsKeyRecordData" />. It lets a validator find the
///     signing key among several without trying every one.
/// </param>
/// <param name="signerName">The name of the zone whose key produced this signature.</param>
/// <param name="signature">The signature, in the form <see cref="Algorithm" /> defines.</param>
public class RrSigRecordData(
    DnsResourceRecordTypeEnum typeCovered,
    DnssecAlgorithmEnum algorithm,
    byte labels,
    uint originalTimeToLiveSeconds,
    uint signatureExpiration,
    uint signatureInception,
    ushort keyTag,
    DnsName signerName,
    byte[] signature
) : IDnsRecordData, IEquatable<RrSigRecordData>
{
    /// <summary>
    ///     This property contains which RRset this signature covers.
    /// </summary>
    public DnsResourceRecordTypeEnum TypeCovered { get; } = typeCovered;

    /// <summary>
    ///     This property contains the signing algorithm.
    /// </summary>
    public DnssecAlgorithmEnum Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains the number of labels in the signed RRset's owner name.
    /// </summary>
    public byte Labels { get; } = labels;

    /// <summary>
    ///     This property contains the signed RRset's TTL in the authoritative zone.
    /// </summary>
    public uint OriginalTimeToLiveSeconds { get; } = originalTimeToLiveSeconds;

    /// <summary>
    ///     This property contains the point after which this signature is no longer valid.
    /// </summary>
    public uint SignatureExpiration { get; } = signatureExpiration;

    /// <summary>
    ///     This property contains the point before which this signature is not yet valid.
    /// </summary>
    public uint SignatureInception { get; } = signatureInception;

    /// <summary>
    ///     This property contains the signing <see cref="DnsKeyRecordData" />'s key tag.
    /// </summary>
    public ushort KeyTag { get; } = keyTag;

    /// <summary>
    ///     This property contains the name of the zone whose key produced this signature.
    /// </summary>
    public DnsName SignerName { get; } = signerName;

    /// <summary>
    ///     This property contains the signature itself.
    /// </summary>
    public byte[] Signature { get; } = signature;

    /// <inheritdoc />
    public bool Equals(RrSigRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                TypeCovered == other.TypeCovered
                && Algorithm == other.Algorithm
                && Labels == other.Labels
                && OriginalTimeToLiveSeconds == other.OriginalTimeToLiveSeconds
                && SignatureExpiration == other.SignatureExpiration
                && SignatureInception == other.SignatureInception
                && KeyTag == other.KeyTag
                && SignerName == other.SignerName
                && Signature.AsSpan().SequenceEqual(other.Signature)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as RrSigRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(TypeCovered);
        hash.Add(Algorithm);
        hash.Add(Labels);
        hash.Add(OriginalTimeToLiveSeconds);
        hash.Add(SignatureExpiration);
        hash.Add(SignatureInception);
        hash.Add(KeyTag);
        hash.Add(SignerName);

        foreach (byte b in Signature)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="RrSigRecordData" />.
/// </summary>
public class RrSigRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.RrSig;

    /// <inheritdoc />
    public Type DataType => typeof(RrSigRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        DnsResourceRecordTypeEnum typeCovered = (DnsResourceRecordTypeEnum)
            BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        DnssecAlgorithmEnum algorithm = (DnssecAlgorithmEnum)rdata[2];
        byte labels = rdata[3];
        uint originalTtl = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(4, 4));
        uint expiration = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(8, 4));
        uint inception = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(12, 4));
        ushort keyTag = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(16, 2));

        // RFC 4034 §3.1.7 forbids compressing the Signer's Name, so UncompressedDnsNameCodec
        // is enough here.
        int offset = 18;
        DnsName signerName = UncompressedDnsNameCodec.Read(rdata, ref offset);
        byte[] signature = rdata[offset..].ToArray();

        return new RrSigRecordData(
            typeCovered,
            algorithm,
            labels,
            originalTtl,
            expiration,
            inception,
            keyTag,
            signerName,
            signature
        );
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        DnsResourceRecordTypeEnum typeCovered = (DnsResourceRecordTypeEnum)
            BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        DnssecAlgorithmEnum algorithm = (DnssecAlgorithmEnum)rdata[2];
        byte labels = rdata[3];
        uint originalTtl = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(4, 4));
        uint expiration = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(8, 4));
        uint inception = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(12, 4));
        ushort keyTag = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(16, 2));

        int offset = 18;
        DnsName signerName = decompressor.Read(ref offset);
        byte[] signature = rdata[offset..].ToArray();

        return new RrSigRecordData(
            typeCovered,
            algorithm,
            labels,
            originalTtl,
            expiration,
            inception,
            keyTag,
            signerName,
            signature
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        RrSigRecordData typed = (RrSigRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(18);
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], (ushort)typed.TypeCovered);
        header[2] = (byte)typed.Algorithm;
        header[3] = typed.Labels;
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), typed.OriginalTimeToLiveSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(8, 4), typed.SignatureExpiration);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(12, 4), typed.SignatureInception);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(16, 2), typed.KeyTag);
        buffer.Advance(18);

        UncompressedDnsNameCodec.Write(buffer, typed.SignerName);

        typed.Signature.CopyTo(buffer.GetSpan(typed.Signature.Length));
        buffer.Advance(typed.Signature.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
