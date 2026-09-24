// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents a single Neighbor Discovery option exactly as it appears on the
///     wire: a 1-byte type and its raw value bytes, per RFC 4861 Section 4.6 -- the Router
///     Advertisement analogue of <see cref="Dhcpv6Option" />, differing in exactly the same way
///     <see cref="RaConstants.OptionHeaderLength" />'s own doc comment already explains (a 1-byte
///     type/1-byte length rather than DHCPv6's 2-byte/2-byte, with length counted in 8-octet units
///     inclusive of the header). Giving meaning to a particular type's value is deliberately kept
///     out of this class; that belongs to <see cref="RaMessageOptionExtensions" />.
/// </summary>
/// <param name="type">This option's 1-byte type.</param>
/// <param name="data">This option's raw value bytes, exactly as they appear on the wire.</param>
public class RaOption(byte type, byte[] data) : IEquatable<RaOption>
{
    /// <summary>
    ///     This property contains this option's 1-byte type.
    /// </summary>
    public byte Type { get; } = type;

    /// <summary>
    ///     This property contains this option's raw value bytes, exactly as they appear on the
    ///     wire -- everything after its own 2-byte type/length header.
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
