// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a single DHCPv6 option exactly as it appears on the wire: a 2-byte
///     "option-code" identifying what it is, and its raw "option-data" value bytes, per RFC 8415
///     Section 21.1. Giving meaning to a particular code's value is deliberately kept out of this
///     class; that belongs to <see cref="Dhcpv6MessageOptionExtensions" />. This class only needs
///     to move an option's bytes faithfully, whether or not anything in this project understands
///     what they mean yet -- the DHCPv6 analogue of <see cref="Dhcpv4Option" />, differing only in
///     that DHCPv6 has no Pad/End structural framing codes to carry separately from an ordinary
///     option (see <see cref="Enum.Dhcpv6OptionCodeEnum" />'s own doc comment).
///     <para>
///         This is a plain class rather than a record for the same reason <see cref="Dhcpv4Option" />
///         is: <see cref="Data" /> is a <see langword="byte[]" />, and the compiler-generated
///         equality a record would give this type compares two arrays by reference, not by
///         content.
///     </para>
/// </summary>
/// <param name="code">This option's 2-byte option code.</param>
/// <param name="data">This option's raw value bytes, exactly as they appear on the wire.</param>
public class Dhcpv6Option(ushort code, byte[] data) : IEquatable<Dhcpv6Option>
{
    /// <summary>
    ///     This property contains this option's 2-byte option code.
    /// </summary>
    public ushort Code { get; } = code;

    /// <summary>
    ///     This property contains this option's raw value bytes, exactly as they appear on the
    ///     wire.
    /// </summary>
    public byte[] Data { get; } = data;

    /// <inheritdoc />
    public bool Equals(Dhcpv6Option other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (Code == other.Code && Data.AsSpan().SequenceEqual(other.Data))
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Dhcpv6Option);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Code);

        foreach (byte dataByte in Data)
            hash.Add(dataByte);

        return hash.ToHashCode();
    }
}
