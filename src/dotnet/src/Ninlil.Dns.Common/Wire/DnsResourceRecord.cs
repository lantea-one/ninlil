using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a single resource record as it actually appears on the wire: a
///     name, a type, a class, a time-to-live, and that type's own data, kept here as an opaque
///     byte sequence rather than a specific, strongly-typed shape. Giving meaning to a
///     particular type's data -- decoding an <see cref="DnsResourceRecordTypeEnum.A" /> record's
///     four bytes into an <see cref="System.Net.IPAddress" />, for example -- is deliberately
///     kept out of this class; that belongs to a record-type-specific codec, registered against
///     <see cref="DnsResourceRecordTypeEnum" />; this class only needs to move the bytes
///     faithfully, whether or not it (or anything else in this project) understands what they mean.
///     <para>
///         This is a plain class rather than a record specifically because <see cref="Data" /> is
///         a <see langword="byte[]" />: a record's compiler-generated equality would compare two
///         arrays by reference rather than by content, silently treating two records with
///         identical bytes in two different array instances as unequal. The equality implemented
///         here compares <see cref="Data" /> by content instead, which matters once zone
///         comparisons (for example, deciding what changed for an incremental zone transfer)
///         start relying on it.
///     </para>
/// </summary>
/// <param name="name">This record's owner name.</param>
/// <param name="type">This record's type.</param>
/// <param name="recordClass">This record's class, almost always <see cref="DnsClassEnum.Internet" />.</param>
/// <param name="timeToLive">
///     How long, in seconds, a resolver that caches this record may keep using its cached copy.
/// </param>
/// <param name="data">This record's type-specific data, exactly as it appears on the wire.</param>
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
    ///     This property contains this record's type-specific data, exactly as it appears on the
    ///     wire.
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
