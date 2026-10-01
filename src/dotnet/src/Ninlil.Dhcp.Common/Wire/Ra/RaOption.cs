// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents one Neighbor Discovery option as it appears on the wire (RFC 4861
///     Section 4.6): a 1-byte type and the raw value bytes after the 2-byte header. It is the
///     counterpart of <see cref="Dhcpv6Option" />; the framing differs as described on
///     <see cref="RaConstants.OptionHeaderLength" />. Interpreting a value belongs to
///     <see cref="RaMessageOptionExtensions" />. Equality compares the bytes, as in
///     <see cref="Dhcpv4Option" />.
/// </summary>
/// <param name="type">This option's 1-byte type.</param>
/// <param name="data">This option's raw value bytes, as they appear on the wire.</param>
public class RaOption(byte type, byte[] data) : IEquatable<RaOption>
{
    /// <summary>
    ///     This property contains this option's 1-byte type.
    /// </summary>
    public byte Type { get; } = type;

    /// <summary>
    ///     This property contains this option's raw value bytes: everything after the 2-byte
    ///     type/length header.
    /// </summary>
    public byte[] Data { get; } = data;

    /// <inheritdoc />
    public bool Equals(RaOption other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (Type == other.Type && Data.AsSpan().SequenceEqual(other.Data))
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as RaOption);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Type);

        foreach (byte dataByte in Data)
            hash.Add(dataByte);

        return hash.ToHashCode();
    }
}
