// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a single DHCP option exactly as it appears on the wire: a one-byte
///     tag identifying what it is, and its raw value bytes. Giving meaning to a particular tag's
///     value -- decoding tag 53's single byte into a <see cref="Enum.Dhcpv4MessageTypeEnum" />,
///     for example -- is deliberately kept out of this class; that belongs to
///     <see cref="Dhcpv4MessageOptionExtensions" />. This class only needs to move an option's
///     bytes faithfully, whether or not anything in this project understands what they mean yet.
///     <para>
///         Pad (tag 0) and End (tag 255) are structural framing bytes, not options with a value,
///         and are never represented as an instance of this class -- <see cref="Dhcpv4MessageReader" />
///         and <see cref="Dhcpv4MessageWriter" /> consume/emit them directly.
///     </para>
///     <para>
///         This is a plain class rather than a record for the same reason
///         <c>Ninlil.Dns.Common.Wire.DnsResourceRecord</c> is: <see cref="Data" /> is a
///         <see langword="byte[]" />, and the compiler-generated equality a record would give this
///         type compares two arrays by reference, not by content -- which matters the moment
///         anything (a future test, a future option-diffing feature) needs to compare two options
///         for equality.
///     </para>
/// </summary>
/// <param name="tag">This option's one-byte tag.</param>
/// <param name="data">This option's raw value bytes, exactly as they appear on the wire.</param>
public class Dhcpv4Option(byte tag, byte[] data) : IEquatable<Dhcpv4Option>
{
    /// <summary>
    ///     This property contains this option's one-byte tag.
    /// </summary>
    public byte Tag { get; } = tag;

    /// <summary>
    ///     This property contains this option's raw value bytes, exactly as they appear on the
    ///     wire.
    /// </summary>
    public byte[] Data { get; } = data;

    /// <inheritdoc />
    public bool Equals(Dhcpv4Option other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (Tag == other.Tag && Data.AsSpan().SequenceEqual(other.Data))
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Dhcpv4Option);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Tag);

        foreach (byte dataByte in Data)
            hash.Add(dataByte);

        return hash.ToHashCode();
    }
}
