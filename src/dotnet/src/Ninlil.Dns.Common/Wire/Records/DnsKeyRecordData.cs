using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data shared by <see cref="DnsResourceRecordTypeEnum.DnsKey" /> and
///     <see cref="DnsResourceRecordTypeEnum.CdnsKey" /> records, which RFC 4034 §2 and RFC 7344
///     define with the same RDATA layout: a public key a resolver uses to validate
///     <see cref="RrSigRecordData" /> signatures. A zone publishes
///     <see cref="DnsResourceRecordTypeEnum.DnsKey" />. A child publishes
///     <see cref="DnsResourceRecordTypeEnum.CdnsKey" /> as the key it wants its parent to publish a
///     <see cref="DsRecordData" /> for, the same automated pairing
///     <see cref="DnsResourceRecordTypeEnum.Cds" /> has with <see cref="DnsResourceRecordTypeEnum.DS" />.
///     <para>
///         This is a plain class rather than a record, since <see cref="PublicKey" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="isZoneKey">
///     Whether this key is a zone key: bit 7 of the Flags field. Every key Ninlil generates sets
///     it, as RFC 4034 §2.1.1 requires for keys that sign zone data.
/// </param>
/// <param name="isSecureEntryPoint">
///     Whether this key is a Secure Entry Point (by convention a key-signing key): bit 15 of the
///     Flags field. RFC 4034 §2.1.1 makes it a hint for signing tools only; it does not change how
///     a validator verifies a signature.
/// </param>
/// <param name="algorithm">This key's cryptographic algorithm.</param>
/// <param name="publicKey">
///     The public key, in the format <see cref="Algorithm" /> defines: exponent then modulus for
///     RSA (RFC 3110), the concatenated curve point for ECDSA (RFC 6605), or the encoded point for
///     EdDSA (RFC 8080).
/// </param>
/// <param name="isRevoked">
///     Whether the key announces its revocation: bit 8 of the Flags field (RFC 5011 §3). A key
///     revokes itself by being republished with this bit set and signed by itself. A validator
///     that verifies that signature stops trusting the key at once, without the hold-down delay
///     of RFC 5011 §2.3. Keys Ninlil signs never set it (Ninlil retires a key by no longer
///     publishing it), so it defaults to <see langword="false" />.
///     <c>TrustAnchorRolloverService</c> reads it from upstream keys.
/// </param>
public class DnsKeyRecordData(
    bool isZoneKey,
    bool isSecureEntryPoint,
    DnssecAlgorithmEnum algorithm,
    byte[] publicKey,
    bool isRevoked = false
) : IDnsRecordData, IEquatable<DnsKeyRecordData>
{
    /// <summary>
    ///     This constant contains the RDATA's Protocol field value. RFC 4034 §2.1.2 fixes it at 3 and
    ///     requires any other value to be treated as invalid.
    /// </summary>
    public const byte Protocol = 3;

    /// <summary>
    ///     This property contains whether this key is a zone key.
    /// </summary>
    public bool IsZoneKey { get; } = isZoneKey;

    /// <summary>
    ///     This property contains whether this key is a Secure Entry Point.
    /// </summary>
    public bool IsSecureEntryPoint { get; } = isSecureEntryPoint;

    /// <summary>
    ///     This property contains whether this key announces its revocation.
    /// </summary>
    public bool IsRevoked { get; } = isRevoked;

    /// <summary>
    ///     This property contains this key's cryptographic algorithm.
    /// </summary>
    public DnssecAlgorithmEnum Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains the public key material itself.
    /// </summary>
    public byte[] PublicKey { get; } = publicKey;

    /// <inheritdoc />
    public bool Equals(DnsKeyRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                IsZoneKey == other.IsZoneKey
                && IsSecureEntryPoint == other.IsSecureEntryPoint
                && IsRevoked == other.IsRevoked
                && Algorithm == other.Algorithm
                && PublicKey.AsSpan().SequenceEqual(other.PublicKey)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DnsKeyRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(IsZoneKey);
        hash.Add(IsSecureEntryPoint);
        hash.Add(IsRevoked);
        hash.Add(Algorithm);

        foreach (byte b in PublicKey)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="DnsKeyRecordData" /> for both
///     <see cref="DnsResourceRecordTypeEnum.DnsKey" /> and <see cref="DnsResourceRecordTypeEnum.CdnsKey" />,
///     since RFC 7344 gives CDNSKEY the same RDATA layout as DNSKEY.
/// </summary>
internal static class DnsKeyCodec
{
    /// <summary>
    ///     This constant contains bit 7 (counting from the most significant bit) of the Flags
    ///     field, the Zone Key flag, per RFC 4034 §2.1.1.
    /// </summary>
    private const ushort ZoneKeyFlag = 1 << 8;

    /// <summary>
    ///     This constant contains bit 15 of the Flags field, the Secure Entry Point flag, per
    ///     RFC 4034 §2.1.1 and RFC 3757.
    /// </summary>
    private const ushort SecureEntryPointFlag = 1;

    /// <summary>
    ///     This constant contains bit 8 (counting from the most significant bit) of the Flags
    ///     field, the Revoke flag, per RFC 5011 §3.
    /// </summary>
    private const ushort RevokeFlag = 1 << 7;

    /// <summary>
    ///     This method decodes a DNSKEY-family record's RDATA.
    /// </summary>
    public static DnsKeyRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);

        // rdata[2] is the Protocol field, always 3 (RFC 4034 §2.1.2), so the model has no
        // property for it.
        DnssecAlgorithmEnum algorithm = (DnssecAlgorithmEnum)rdata[3];
        byte[] publicKey = rdata[4..].ToArray();

        return new DnsKeyRecordData(
            (flags & ZoneKeyFlag) != 0,
            (flags & SecureEntryPointFlag) != 0,
            algorithm,
            publicKey,
            (flags & RevokeFlag) != 0
        );
    }

    /// <summary>
    ///     This method encodes a DNSKEY-family record's RDATA.
    /// </summary>
    public static byte[] Write(DnsKeyRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();

        ushort flags = 0;

        if (data.IsZoneKey)
            flags |= ZoneKeyFlag;

        if (data.IsSecureEntryPoint)
            flags |= SecureEntryPointFlag;

        if (data.IsRevoked)
            flags |= RevokeFlag;

        Span<byte> header = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt16BigEndian(header, flags);
        header[2] = DnsKeyRecordData.Protocol;
        header[3] = (byte)data.Algorithm;
        buffer.Advance(4);

        data.PublicKey.CopyTo(buffer.GetSpan(data.PublicKey.Length));
        buffer.Advance(data.PublicKey.Length);

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
///     This class reads and writes <see cref="DnsKeyRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.DnsKey" /> record type.
/// </summary>
public class DnsKeyRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.DnsKey;

    /// <inheritdoc />
    public Type DataType => typeof(DnsKeyRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => DnsKeyCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => DnsKeyCodec.Write((DnsKeyRecordData)data);
}

/// <summary>
///     This class reads and writes <see cref="DnsKeyRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.CdnsKey" /> record type.
/// </summary>
public class CdnsKeyRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.CdnsKey;

    /// <inheritdoc />
    public Type DataType => typeof(DnsKeyRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => DnsKeyCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => DnsKeyCodec.Write((DnsKeyRecordData)data);
}
