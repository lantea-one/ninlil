// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents one DHCPv4 option as it appears on the wire: a one-byte tag and the
///     raw value bytes. Interpreting a value (decoding tag 53 into a
///     <see cref="Enum.Dhcpv4MessageTypeEnum" />, for example) belongs to
///     <see cref="Dhcpv4MessageOptionExtensions" />; this class carries the bytes whether or not
///     the tag is understood.
///     <para>
///         Pad (tag 0) and End (tag 255) are framing bytes, not options, and are never
///         represented by this class; <see cref="Dhcpv4MessageReader" /> and
///         <see cref="Dhcpv4MessageWriter" /> handle them directly.
///     </para>
///     <para>
///         This is a class rather than a record, as <c>Ninlil.Dns.Common.Wire.DnsResourceRecord</c>
///         is: <see cref="Data" /> is a <see langword="byte[]" />, and record equality would
///         compare the arrays by reference. <see cref="Equals(Dhcpv4Option)" /> compares content.
///     </para>
/// </summary>
/// <param name="tag">This option's one-byte tag.</param>
/// <param name="data">This option's raw value bytes, as they appear on the wire.</param>
public class Dhcpv4Option(byte tag, byte[] data) : IEquatable<Dhcpv4Option>
{
    /// <summary>
    ///     This property contains this option's one-byte tag.
    /// </summary>
    public byte Tag { get; } = tag;

    /// <summary>
    ///     This property contains this option's raw value bytes, as they appear on the wire.
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
