using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Nsec3Param" />
///     record, defined by RFC 5155 §4. It sits at a signed zone's apex and gives the hash
///     algorithm, iteration count and salt the zone's <see cref="Nsec3RecordData" /> chain uses.
///     Authoritative servers read it to know which hashes to compute for a query.
///     <para>
///         This is a plain class rather than a record, since <see cref="Salt" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="hashAlgorithm">The same meaning as <see cref="Nsec3RecordData.HashAlgorithm" />.</param>
/// <param name="iterations">The same meaning as <see cref="Nsec3RecordData.Iterations" />.</param>
/// <param name="salt">The same meaning as <see cref="Nsec3RecordData.Salt" />.</param>
public class Nsec3ParamRecordData(byte hashAlgorithm, ushort iterations, byte[] salt)
    : IDnsRecordData,
        IEquatable<Nsec3ParamRecordData>
{
    /// <summary>
    ///     This property contains the hash algorithm this zone's NSEC3 chain uses.
    /// </summary>
    public byte HashAlgorithm { get; } = hashAlgorithm;

    /// <summary>
    ///     This property contains how many additional times this zone's NSEC3 chain applies the
    ///     hash.
    /// </summary>
    public ushort Iterations { get; } = iterations;

    /// <summary>
    ///     This property contains the salt this zone's NSEC3 chain mixes into every hash.
    /// </summary>
    public byte[] Salt { get; } = salt;

    /// <inheritdoc />
    public bool Equals(Nsec3ParamRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                HashAlgorithm == other.HashAlgorithm
                && Iterations == other.Iterations
                && Salt.AsSpan().SequenceEqual(other.Salt)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Nsec3ParamRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(HashAlgorithm);
        hash.Add(Iterations);

        foreach (byte b in Salt)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="Nsec3ParamRecordData" />.
/// </summary>
public class Nsec3ParamRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Nsec3Param;

    /// <inheritdoc />
    public Type DataType => typeof(Nsec3ParamRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        byte hashAlgorithm = rdata[0];

        // rdata[1] is the Flags field. RFC 5155 §4.1.2 defines no NSEC3PARAM flags (Opt-Out
        // belongs to each NSEC3 record, not the zone), so the model has no property for it.
        ushort iterations = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        byte saltLength = rdata[4];
        byte[] salt = rdata.Slice(5, saltLength).ToArray();

        return new Nsec3ParamRecordData(hashAlgorithm, iterations, salt);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        Nsec3ParamRecordData typed = (Nsec3ParamRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(5);
        header[0] = typed.HashAlgorithm;
        header[1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), typed.Iterations);
        header[4] = (byte)typed.Salt.Length;
        buffer.Advance(5);

        typed.Salt.CopyTo(buffer.GetSpan(typed.Salt.Length));
        buffer.Advance(typed.Salt.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
