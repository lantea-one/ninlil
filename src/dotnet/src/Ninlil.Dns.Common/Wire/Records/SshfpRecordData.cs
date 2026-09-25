using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.SshFp" />
///     record, defined by RFC 4255: an SSH host key's fingerprint, which lets a client verify a
///     server's key through DNSSEC instead of trusting it on first connection.
///     <para>
///         This is a plain class rather than a record, since <see cref="Fingerprint" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="algorithm">
///     The public key's algorithm, per IANA's "SSHFP RR Types for public key algorithms"
///     registry: 1 is RSA, 2 is DSA (RFC 4255), 3 is ECDSA (RFC 6594), 4 is Ed25519 (RFC 7479)
///     and 6 is Ed448 (RFC 8709). It is a raw byte rather than an enum, like
///     <see cref="CaaRecordData.Tag" /> and <see cref="AplItem.AddressFamily" />, because the
///     registry is specific to this record type.
/// </param>
/// <param name="fingerprintType">
///     The fingerprint's hash algorithm, per IANA's "SSHFP RR Types for fingerprint types"
///     registry: 1 is SHA-1 (RFC 4255), 2 is SHA-256 (RFC 6594).
/// </param>
/// <param name="fingerprint">The fingerprint's raw hash bytes.</param>
public class SshfpRecordData(byte algorithm, byte fingerprintType, byte[] fingerprint)
    : IDnsRecordData,
        IEquatable<SshfpRecordData>
{
    /// <summary>
    ///     This property contains the public key's algorithm.
    /// </summary>
    public byte Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains the fingerprint's hash algorithm.
    /// </summary>
    public byte FingerprintType { get; } = fingerprintType;

    /// <summary>
    ///     This property contains the fingerprint's raw hash bytes.
    /// </summary>
    public byte[] Fingerprint { get; } = fingerprint;

    /// <inheritdoc />
    public bool Equals(SshfpRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                Algorithm == other.Algorithm
                && FingerprintType == other.FingerprintType
                && Fingerprint.AsSpan().SequenceEqual(other.Fingerprint)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as SshfpRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Algorithm);
        hash.Add(FingerprintType);

        foreach (byte b in Fingerprint)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="SshfpRecordData" />.
/// </summary>
public class SshfpRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.SshFp;

    /// <inheritdoc />
    public Type DataType => typeof(SshfpRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new SshfpRecordData(rdata[0], rdata[1], rdata[2..].ToArray());

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        SshfpRecordData typed = (SshfpRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(2);
        header[0] = typed.Algorithm;
        header[1] = typed.FingerprintType;
        buffer.Advance(2);

        typed.Fingerprint.CopyTo(buffer.GetSpan(typed.Fingerprint.Length));
        buffer.Advance(typed.Fingerprint.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
