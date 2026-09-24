using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data shared by <see cref="DnsResourceRecordTypeEnum.DnsKey" /> and
///     <see cref="DnsResourceRecordTypeEnum.CdnsKey" /> records -- RFC 4034 §2 and RFC 7344
///     respectively define both with an identical RDATA shape, a public key a resolver uses to
///     validate <see cref="RrSigRecordData" /> signatures. <see cref="DnsResourceRecordTypeEnum.DnsKey" />
///     is what a zone actually publishes; <see cref="DnsResourceRecordTypeEnum.CdnsKey" /> is the
///     child's own copy of what it wants a parent to publish as a <see cref="DsRecordData" />,
///     the same automated-update pairing <see cref="DnsResourceRecordTypeEnum.Cds" /> has with
///     <see cref="DnsResourceRecordTypeEnum.DS" />.
///     <para>
///         This is a plain class rather than a record, since <see cref="PublicKey" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="isZoneKey">
///     Whether this key is a zone key -- bit 7 of the RDATA's Flags field. Every key this
///     project generates sets this, per RFC 4034 §2.1.1; the field exists at all only because the
///     original design once imagined non-zone DNSSEC keys that were never actually standardized.
/// </param>
/// <param name="isSecureEntryPoint">
///     Whether this key is a Secure Entry Point (a key-signing key, conventionally) -- bit 15 of
///     the Flags field. Per RFC 4034 §2.1.1, this is purely a hint for zone-signing tooling; it
///     has no effect on how a validator actually verifies a signature.
/// </param>
/// <param name="algorithm">This key's cryptographic algorithm.</param>
/// <param name="publicKey">
///     The public key material itself, encoded in whatever format <see cref="Algorithm" />
///     defines -- RFC 3110's exponent-then-modulus form for the RSA family, the raw concatenated
///     curve point for ECDSA (RFC 6605), or the raw encoded point for EdDSA (RFC 8080).
/// </param>
/// <param name="isRevoked">
///     Whether this key is announcing its own revocation -- bit 8 of the Flags field, per RFC
///     5011 §3. A key does this by re-publishing itself with this bit set, self-signed with
///     that same key; a validator that sees this (and verifies the self-signature) must stop
///     trusting the key immediately, with no RFC 5011 §2.3 hold-down delay, since this is a
///     deliberate signal rather than a passive absence. Never set by anything this project
///     itself signs (a project always retracts a key by simply stopping publication of it, not
///     by revocation), so this defaults to <see langword="false" /> for every existing caller.
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
    ///     This constant contains the RDATA's Protocol field value -- RFC 4034 §2.1.2 fixes this
    ///     at 3 for every DNSKEY that has ever existed, and requires treating any other value as
    ///     invalid; it is not a configurable choice.
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
    ///     This property contains whether this key is announcing its own revocation.
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
///     since RFC 7344 gives the latter an identical RDATA shape to the former.
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

        // rdata[2] is the Protocol field, always 3 per RFC 4034 §2.1.2; there is nothing else it
        // could legally be, so there is no separate model property to carry it.
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
