// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents one DHCPv6 option as it appears on the wire: a 2-byte
///     "option-code" and the raw "option-data" bytes (RFC 8415 Section 21.1). Interpreting a value
///     belongs to <see cref="Dhcpv6MessageOptionExtensions" />; this class carries the bytes
///     whether or not the code is understood. It is the DHCPv6 counterpart of
///     <see cref="Dhcpv4Option" />.
///     <para>
///         It is a class rather than a record for the same reason as <see cref="Dhcpv4Option" />:
///         record equality would compare the <see cref="Data" /> arrays by reference.
///     </para>
/// </summary>
/// <param name="code">This option's 2-byte option code.</param>
/// <param name="data">This option's raw value bytes, as they appear on the wire.</param>
public class Dhcpv6Option(ushort code, byte[] data) : IEquatable<Dhcpv6Option>
{
    /// <summary>
    ///     This property contains this option's 2-byte option code.
    /// </summary>
    public ushort Code { get; } = code;

    /// <summary>
    ///     This property contains this option's raw value bytes, as they appear on the wire.
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
