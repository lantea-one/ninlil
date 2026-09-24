using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Nsec3" />
///     record, defined by RFC 5155 §3: does the same job as <see cref="NsecRecordData" /> --
///     proving a name's non-existence -- but names its neighbor by a salted, iterated hash of
///     the owner name rather than the plain name itself, so that walking a zone by repeatedly
///     asking for the "next" nonexistent name no longer reveals every real name in it.
///     <para>
///         This is a plain class rather than a record, since <see cref="Salt" /> and
///         <see cref="NextHashedOwnerName" /> are <see langword="byte[]" />s and a
///         compiler-generated equality would compare them by reference rather than by content.
///     </para>
/// </summary>
/// <param name="hashAlgorithm">
///     The hash algorithm used to produce <see cref="NextHashedOwnerName" />, per IANA's "NSEC3
///     Hash Algorithms" registry -- 1 is SHA-1, the only value ever assigned.
/// </param>
/// <param name="isOptOut">
///     Whether this record may also cover an insecure (unsigned) delegation, per RFC 5155 §3
///     and §6 -- the least significant bit of the RDATA's Flags field. Set on the NSEC3 records
///     bracketing an unsigned child zone's delegation point, so proving that child's
///     non-existence does not also require signing every unsigned delegation individually.
/// </param>
/// <param name="iterations">
///     How many additional times the hash is applied, per RFC 5155 §5 -- raises the cost of a
///     dictionary attack against the salted names at the price of more validator CPU time.
/// </param>
/// <param name="salt">
///     A value mixed into every hash in this zone, changed periodically so a
///     precomputed dictionary attack against one snapshot of the zone cannot be reused against
///     a later one.
/// </param>
/// <param name="nextHashedOwnerName">
///     The hashed form of the next owner name in hash order -- raw binary, per RFC 5155 §3.3,
///     never the base32hex text form that name uses when written as this record's own owner
///     name.
/// </param>
/// <param name="types">Every record type present at the *original*, unhashed owner name.</param>
public class Nsec3RecordData(
    byte hashAlgorithm,
    bool isOptOut,
    ushort iterations,
    byte[] salt,
    byte[] nextHashedOwnerName,
    IReadOnlyList<DnsResourceRecordTypeEnum> types
) : IDnsRecordData, IEquatable<Nsec3RecordData>
{
    /// <summary>
    ///     This property contains the hash algorithm used to produce
    ///     <see cref="NextHashedOwnerName" />.
    /// </summary>
    public byte HashAlgorithm { get; } = hashAlgorithm;

    /// <summary>
    ///     This property contains whether this record may also cover an insecure delegation.
    /// </summary>
    public bool IsOptOut { get; } = isOptOut;

    /// <summary>
    ///     This property contains how many additional times the hash is applied.
    /// </summary>
    public ushort Iterations { get; } = iterations;

    /// <summary>
    ///     This property contains the salt mixed into every hash in this zone.
    /// </summary>
    public byte[] Salt { get; } = salt;

    /// <summary>
    ///     This property contains the hashed form of the next owner name in hash order.
    /// </summary>
    public byte[] NextHashedOwnerName { get; } = nextHashedOwnerName;

    /// <summary>
    ///     This property contains every record type present at the original, unhashed owner name.
    /// </summary>
    public IReadOnlyList<DnsResourceRecordTypeEnum> Types { get; } = types;

    /// <inheritdoc />
    public bool Equals(Nsec3RecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                HashAlgorithm == other.HashAlgorithm
                && IsOptOut == other.IsOptOut
                && Iterations == other.Iterations
                && Salt.AsSpan().SequenceEqual(other.Salt)
                && NextHashedOwnerName.AsSpan().SequenceEqual(other.NextHashedOwnerName)
                && Types.SequenceEqual(other.Types)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Nsec3RecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(HashAlgorithm);
        hash.Add(IsOptOut);
        hash.Add(Iterations);

        foreach (byte b in Salt)
            hash.Add(b);

        foreach (byte b in NextHashedOwnerName)
            hash.Add(b);

        foreach (DnsResourceRecordTypeEnum type in Types)
            hash.Add(type);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="Nsec3RecordData" />.
/// </summary>
public class Nsec3RecordDataCodec : IDnsRecordDataCodec
{
    /// <summary>
    ///     This constant contains the Opt-Out flag's bit position within the Flags field -- the
    ///     least significant bit, per RFC 5155 §3.1.2.1.
    /// </summary>
    private const byte OptOutFlag = 0x01;

    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Nsec3;

    /// <inheritdoc />
    public Type DataType => typeof(Nsec3RecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        byte hashAlgorithm = rdata[0];
        bool isOptOut = (rdata[1] & OptOutFlag) != 0;
        ushort iterations = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));

        // The salt and the hashed next name are each a one-octet length prefix followed by
        // that many raw octets, per RFC 5155 §3.3 -- neither field has a fixed size.
        byte saltLength = rdata[4];
        int offset = 5;
        byte[] salt = rdata.Slice(offset, saltLength).ToArray();
        offset += saltLength;

        byte hashLength = rdata[offset];
        offset += 1;
        byte[] nextHashedOwnerName = rdata.Slice(offset, hashLength).ToArray();
        offset += hashLength;

        // Whatever remains after both variable-length fields is the shared Type Bit Maps
        // encoding (see NsecTypeBitmapCodec), listing every type present at the owner name.
        List<DnsResourceRecordTypeEnum> types = NsecTypeBitmapCodec.Read(rdata[offset..]);

        return new Nsec3RecordData(
            hashAlgorithm,
            isOptOut,
            iterations,
            salt,
            nextHashedOwnerName,
            types
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        Nsec3RecordData typed = (Nsec3RecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(5);
        header[0] = typed.HashAlgorithm;
        header[1] = typed.IsOptOut ? OptOutFlag : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), typed.Iterations);
        header[4] = (byte)typed.Salt.Length;
        buffer.Advance(5);

        typed.Salt.CopyTo(buffer.GetSpan(typed.Salt.Length));
        buffer.Advance(typed.Salt.Length);

        Span<byte> hashLength = buffer.GetSpan(1);
        hashLength[0] = (byte)typed.NextHashedOwnerName.Length;
        buffer.Advance(1);

        typed.NextHashedOwnerName.CopyTo(buffer.GetSpan(typed.NextHashedOwnerName.Length));
        buffer.Advance(typed.NextHashedOwnerName.Length);

        NsecTypeBitmapCodec.Write(buffer, typed.Types);

        return buffer.WrittenSpan.ToArray();
    }
}
