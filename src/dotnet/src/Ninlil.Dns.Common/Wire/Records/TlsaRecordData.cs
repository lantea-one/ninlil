using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data shared by <see cref="DnsResourceRecordTypeEnum.Tlsa" />
///     and <see cref="DnsResourceRecordTypeEnum.SmimeA" /> records: the DANE association between a
///     name and a TLS certificate (RFC 6698), and the same association for an S/MIME certificate
///     (RFC 8162). Both use the same RDATA layout. They differ only in who looks them up (a TLS
///     client or a mail client) and how the owner name is built.
///     <para>
///         This is a plain class rather than a record, since <see cref="CertificateAssociationData" />
///         is a <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="certificateUsage">
///     What the association asserts (RFC 6698 §2.1.1). 0 (PKIX-TA) and 1 (PKIX-EE) still require
///     a valid PKIX certification path as well; 2 (DANE-TA) and 3 (DANE-EE) trust this record
///     alone, without the certificate authority system.
/// </param>
/// <param name="selector">
///     Which part of the certificate <see cref="CertificateAssociationData" /> was computed
///     from (RFC 6698 §2.1.2): 0 is the full certificate, 1 is its subject public key.
/// </param>
/// <param name="matchingType">
///     How <see cref="CertificateAssociationData" /> was derived (RFC 6698 §2.1.3): 0 is the
///     selected content itself, 1 is its SHA-256 hash, 2 is its SHA-512 hash.
/// </param>
/// <param name="certificateAssociationData">The association data itself.</param>
public class TlsaRecordData(
    byte certificateUsage,
    byte selector,
    byte matchingType,
    byte[] certificateAssociationData
) : IDnsRecordData, IEquatable<TlsaRecordData>
{
    /// <summary>
    ///     This property contains what the association asserts.
    /// </summary>
    public byte CertificateUsage { get; } = certificateUsage;

    /// <summary>
    ///     This property contains which part of the certificate <see cref="CertificateAssociationData" />
    ///     was computed from.
    /// </summary>
    public byte Selector { get; } = selector;

    /// <summary>
    ///     This property contains how <see cref="CertificateAssociationData" /> was derived.
    /// </summary>
    public byte MatchingType { get; } = matchingType;

    /// <summary>
    ///     This property contains the association data itself.
    /// </summary>
    public byte[] CertificateAssociationData { get; } = certificateAssociationData;

    /// <inheritdoc />
    public bool Equals(TlsaRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                CertificateUsage == other.CertificateUsage
                && Selector == other.Selector
                && MatchingType == other.MatchingType
                && CertificateAssociationData
                    .AsSpan()
                    .SequenceEqual(other.CertificateAssociationData)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as TlsaRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(CertificateUsage);
        hash.Add(Selector);
        hash.Add(MatchingType);

        foreach (byte b in CertificateAssociationData)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class contains the wire encoding <see cref="TlsaRecordDataCodec" /> and
///     <see cref="SmimeaRecordDataCodec" /> share, since RFC 6698 and RFC 8162 give both record
///     types the same layout.
/// </summary>
internal static class TlsaCodec
{
    /// <summary>
    ///     This method decodes a TLSA-family record's RDATA.
    /// </summary>
    public static TlsaRecordData Read(ReadOnlySpan<byte> rdata) =>
        new(rdata[0], rdata[1], rdata[2], rdata[3..].ToArray());

    /// <summary>
    ///     This method encodes a TLSA-family record's RDATA.
    /// </summary>
    public static byte[] Write(TlsaRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(3);
        header[0] = data.CertificateUsage;
        header[1] = data.Selector;
        header[2] = data.MatchingType;
        buffer.Advance(3);

        data.CertificateAssociationData.CopyTo(
            buffer.GetSpan(data.CertificateAssociationData.Length)
        );
        buffer.Advance(data.CertificateAssociationData.Length);

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
///     This class reads and writes <see cref="TlsaRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.Tlsa" /> record type.
/// </summary>
public class TlsaRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Tlsa;

    /// <inheritdoc />
    public Type DataType => typeof(TlsaRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => TlsaCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => TlsaCodec.Write((TlsaRecordData)data);
}

/// <summary>
///     This class reads and writes <see cref="TlsaRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.SmimeA" /> record type.
/// </summary>
public class SmimeaRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.SmimeA;

    /// <inheritdoc />
    public Type DataType => typeof(TlsaRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => TlsaCodec.Read(rdata);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) => TlsaCodec.Write((TlsaRecordData)data);
}
