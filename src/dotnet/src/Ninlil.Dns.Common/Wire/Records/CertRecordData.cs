using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Cert" /> record,
///     defined by RFC 4398: a certificate or certificate revocation list published for the
///     owner name, in whichever format <see cref="CertificateType" /> identifies.
///     <para>
///         This is a plain class rather than a record, since <see cref="Certificate" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="certificateType">
///     The certificate's format, per IANA's "Certificate Type Values" registry; for example 1 is
///     PKIX (an X.509 certificate) and 3 is PGP. It is a raw <see langword="ushort" /> rather than
///     an enum, like <see cref="SshfpRecordData.Algorithm" />, because the registry is specific to
///     this record type.
/// </param>
/// <param name="keyTag">
///     The same key tag algorithm defined for <c>DNSKEY</c> records (RFC 4034 Appendix B),
///     letting a client match this certificate to a specific key without inspecting the
///     certificate itself.
/// </param>
/// <param name="algorithm">
///     The certificate's public key algorithm, drawn from the same "DNSSEC Algorithm Numbers"
///     registry <see cref="DnssecAlgorithmEnum" /> models, or <see langword="null" /> when
///     RFC 4398 §2.1's zero value ("no algorithm used with the certificate") applies.
/// </param>
/// <param name="certificate">The certificate or certificate revocation list itself.</param>
public class CertRecordData(
    ushort certificateType,
    ushort keyTag,
    DnssecAlgorithmEnum? algorithm,
    byte[] certificate
) : IDnsRecordData, IEquatable<CertRecordData>
{
    /// <summary>
    ///     This property contains the certificate's format.
    /// </summary>
    public ushort CertificateType { get; } = certificateType;

    /// <summary>
    ///     This property contains the key tag this certificate corresponds to.
    /// </summary>
    public ushort KeyTag { get; } = keyTag;

    /// <summary>
    ///     This property contains the certificate's public key algorithm, or
    ///     <see langword="null" /> when no algorithm is indicated.
    /// </summary>
    public DnssecAlgorithmEnum? Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains the certificate or certificate revocation list itself.
    /// </summary>
    public byte[] Certificate { get; } = certificate;

    /// <inheritdoc />
    public bool Equals(CertRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                CertificateType == other.CertificateType
                && KeyTag == other.KeyTag
                && Algorithm == other.Algorithm
                && Certificate.AsSpan().SequenceEqual(other.Certificate)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as CertRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(CertificateType);
        hash.Add(KeyTag);
        hash.Add(Algorithm);

        foreach (byte b in Certificate)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="CertRecordData" />.
/// </summary>
public class CertRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Cert;

    /// <inheritdoc />
    public Type DataType => typeof(CertRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort certificateType = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort keyTag = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        byte algorithmValue = rdata[4];
        DnssecAlgorithmEnum? algorithm =
            algorithmValue == 0 ? null : (DnssecAlgorithmEnum)algorithmValue;
        byte[] certificate = rdata[5..].ToArray();

        return new CertRecordData(certificateType, keyTag, algorithm, certificate);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        CertRecordData typed = (CertRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(5);
        BinaryPrimitives.WriteUInt16BigEndian(header, typed.CertificateType);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], typed.KeyTag);
        header[4] = typed.Algorithm is { } algorithm ? (byte)algorithm : (byte)0;
        buffer.Advance(5);

        typed.Certificate.CopyTo(buffer.GetSpan(typed.Certificate.Length));
        buffer.Advance(typed.Certificate.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
