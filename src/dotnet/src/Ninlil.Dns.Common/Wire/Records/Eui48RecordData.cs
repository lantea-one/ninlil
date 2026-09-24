using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Eui48" /> record,
///     defined by RFC 7043: a 48-bit (six-octet) identifier, most often a network interface's MAC
///     address, associated with the owner name.
///     <para>
///         Like <see cref="DnsResourceRecord" /> before it, this is a plain class rather than a
///         record, since <see cref="Identifier" /> is a <see langword="byte[]" /> and a
///         compiler-generated equality would compare it by reference rather than by content.
///     </para>
/// </summary>
/// <param name="identifier">The 48-bit identifier's six octets.</param>
public class Eui48RecordData(byte[] identifier) : IDnsRecordData, IEquatable<Eui48RecordData>
{
    /// <summary>
    ///     This property contains the 48-bit identifier's six octets.
    /// </summary>
    public byte[] Identifier { get; } = identifier;

    /// <inheritdoc />
    public bool Equals(Eui48RecordData other) =>
        other is not null
        && (ReferenceEquals(this, other) || Identifier.AsSpan().SequenceEqual(other.Identifier));

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Eui48RecordData);

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
///     This class reads and writes <see cref="Eui48RecordData" />.
/// </summary>
public class Eui48RecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Eui48;

    /// <inheritdoc />
    public Type DataType => typeof(Eui48RecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new Eui48RecordData(rdata[..6].ToArray());

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        Eui48RecordData typed = (Eui48RecordData)data;

        if (typed.Identifier.Length != 6)
            throw new ArgumentException(
                $"An EUI48 record's identifier must be exactly 6 octets; got {typed.Identifier.Length}.",
                nameof(data)
            );

        return typed.Identifier;
    }
}
