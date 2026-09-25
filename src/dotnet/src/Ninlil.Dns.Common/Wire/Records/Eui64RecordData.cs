using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Eui64" /> record,
///     defined by RFC 7043: a 64-bit (eight-octet) identifier associated with the owner name.
///     <para>
///         Like <see cref="Eui48RecordData" />, this is a plain class rather than a record because
///         <see cref="Identifier" /> is a <see langword="byte[]" />.
///     </para>
/// </summary>
/// <param name="identifier">The 64-bit identifier's eight octets.</param>
public class Eui64RecordData(byte[] identifier) : IDnsRecordData, IEquatable<Eui64RecordData>
{
    /// <summary>
    ///     This property contains the 64-bit identifier's eight octets.
    /// </summary>
    public byte[] Identifier { get; } = identifier;

    /// <inheritdoc />
    public bool Equals(Eui64RecordData other) =>
        other is not null
        && (ReferenceEquals(this, other) || Identifier.AsSpan().SequenceEqual(other.Identifier));

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Eui64RecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (byte b in Identifier)
            hash.Add(b);
        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="Eui64RecordData" />.
/// </summary>
public class Eui64RecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Eui64;

    /// <inheritdoc />
    public Type DataType => typeof(Eui64RecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new Eui64RecordData(rdata[..8].ToArray());

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        Eui64RecordData typed = (Eui64RecordData)data;

        if (typed.Identifier.Length != 8)
            throw new ArgumentException(
                $"An EUI64 record's identifier must be exactly 8 octets; got {typed.Identifier.Length}.",
                nameof(data)
            );

        return typed.Identifier;
    }
}
