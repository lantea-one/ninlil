using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents one resource record as it appears on the wire: a name, a type, a
///     class, a time-to-live and the type's data as opaque bytes. Decoding the data (for example
///     the four bytes of an <see cref="DnsResourceRecordTypeEnum.A" /> record into an
///     <see cref="System.Net.IPAddress" />) is the job of the type's codec in
///     <see cref="Records.DnsRecordDataCodecRegistry" />. This class only carries the bytes, so it
///     works for types nothing in Ninlil understands.
///     <para>
///         It is a plain class rather than a record because <see cref="Data" /> is a
///         <see langword="byte[]" />. Compiler-generated equality would compare the arrays by
///         reference, so two records with the same bytes in different arrays would be unequal.
///         The equality here compares <see cref="Data" /> by content, which zone comparisons (such
///         as working out an incremental transfer) rely on.
///     </para>
/// </summary>
/// <param name="name">This record's owner name.</param>
/// <param name="type">This record's type.</param>
/// <param name="recordClass">This record's class, almost always <see cref="DnsClassEnum.Internet" />.</param>
/// <param name="timeToLive">
///     How long, in seconds, a resolver that caches this record may keep using its cached copy.
/// </param>
/// <param name="data">This record's type-specific data, as it appears on the wire.</param>
public class DnsResourceRecord(
    DnsName name,
    DnsResourceRecordTypeEnum type,
    DnsClassEnum recordClass,
    uint timeToLive,
    byte[] data
) : IEquatable<DnsResourceRecord>
{
    /// <summary>
    ///     This property contains this record's owner name.
    /// </summary>
    public DnsName Name { get; } = name;

    /// <summary>
    ///     This property contains this record's type.
    /// </summary>
    public DnsResourceRecordTypeEnum Type { get; } = type;

    /// <summary>
    ///     This property contains this record's class.
    /// </summary>
    public DnsClassEnum Class { get; } = recordClass;

    /// <summary>
    ///     This property contains how long, in seconds, a resolver that caches this record may
    ///     keep using its cached copy.
    /// </summary>
    public uint TimeToLive { get; } = timeToLive;

    /// <summary>
    ///     This property contains this record's type-specific data, as it appears on the wire.
    /// </summary>
    public byte[] Data { get; } = data;

    /// <inheritdoc />
    public bool Equals(DnsResourceRecord other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                Name == other.Name
                && Type == other.Type
                && Class == other.Class
                && TimeToLive == other.TimeToLive
                && Data.AsSpan().SequenceEqual(other.Data)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DnsResourceRecord);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Name);
        hash.Add(Type);
        hash.Add(Class);
        hash.Add(TimeToLive);

        foreach (byte b in Data)
            hash.Add(b);

        return hash.ToHashCode();
    }

    /// <summary>
    ///     This operator compares two records for content equality.
    /// </summary>
    public static bool operator ==(DnsResourceRecord left, DnsResourceRecord right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    ///     This operator compares two records for content inequality.
    /// </summary>
    public static bool operator !=(DnsResourceRecord left, DnsResourceRecord right) =>
        !(left == right);
}
