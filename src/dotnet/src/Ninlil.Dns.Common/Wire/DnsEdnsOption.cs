using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents one EDNS(0) option inside an OPT pseudo-record (RFC 6891).
///     <see cref="Code" /> is a raw <see langword="ushort" /> rather than an
///     <see cref="EdnsOptionCodeEnum" />, because RFC 6891 requires unknown options to be kept and
///     ignored rather than rejected. <see cref="TryGetKnownCode" /> says whether Ninlil knows the
///     code.
/// </summary>
/// <param name="code">This option's raw, numeric option code.</param>
/// <param name="data">This option's data, as it appears on the wire.</param>
public class DnsEdnsOption(ushort code, byte[] data) : IEquatable<DnsEdnsOption>
{
    /// <summary>
    ///     This property contains this option's raw, numeric option code.
    /// </summary>
    public ushort Code { get; } = code;

    /// <summary>
    ///     This property contains this option's data, as it appears on the wire.
    /// </summary>
    public byte[] Data { get; } = data;

    /// <summary>
    ///     This method checks whether <see cref="Code" /> is one of the option codes Ninlil
    ///     recognizes.
    /// </summary>
    /// <param name="knownCode">
    ///     The recognized <see cref="EdnsOptionCodeEnum" /> value, if <see cref="Code" /> matched
    ///     one.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether <see cref="Code" /> was recognized.</returns>
    public bool TryGetKnownCode(out EdnsOptionCodeEnum knownCode)
    {
        if (System.Enum.IsDefined(typeof(EdnsOptionCodeEnum), Code))
        {
            knownCode = (EdnsOptionCodeEnum)Code;
            return true;
        }

        knownCode = default;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(DnsEdnsOption other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (Code == other.Code && Data.AsSpan().SequenceEqual(other.Data))
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DnsEdnsOption);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Code);

        foreach (byte b in Data)
            hash.Add(b);

        return hash.ToHashCode();
    }
}
