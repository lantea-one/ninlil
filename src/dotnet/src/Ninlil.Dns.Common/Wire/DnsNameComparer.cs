// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class compares and hashes <see cref="DnsName" /> keys exactly as the name's own
///     equality does (ASCII case-insensitive per label, RFC 4343 §3), and also compares a
///     <see cref="DnsNameSuffix" /> with a stored name. A dictionary built with this comparer
///     answers "is the suffix of this query name that skips n labels a key?" through
///     <c>GetAlternateLookup&lt;DnsNameSuffix&gt;()</c> without building a <see cref="DnsName" />
///     per suffix. The zone cache and the forwarder set snapshot use it to find the closest
///     enclosing zone of a query name.
///     <para>
///         The comparison works on whole labels, never on text, so an escaped dot stays inside its
///         label and <c>badexample.com</c> is not a suffix match for <c>example.com</c>. The
///         hash of a suffix is the hash its <see cref="DnsName" /> would have, so the two lookup
///         forms always agree.
///     </para>
/// </summary>
public sealed class DnsNameComparer
    : IEqualityComparer<DnsName>,
        IAlternateEqualityComparer<DnsNameSuffix, DnsName>
{
    /// <summary>
    ///     This property contains the shared, stateless instance.
    /// </summary>
    public static DnsNameComparer Instance { get; } = new();

    /// <inheritdoc />
    public bool Equals(DnsName x, DnsName y) => x is null ? y is null : x.Equals(y);

    /// <inheritdoc />
    public int GetHashCode(DnsName obj) => obj.GetHashCode();

    /// <inheritdoc />
    public bool Equals(DnsNameSuffix alternate, DnsName other) =>
        other is not null
        && DnsName.LabelsEqual(alternate.Name.Labels, alternate.SkipLabels, other.Labels);

    /// <inheritdoc />
    public int GetHashCode(DnsNameSuffix alternate) =>
        alternate.SkipLabels == 0
            ? alternate.Name.GetHashCode()
            : DnsName.ComputeHash(alternate.Name.Labels, alternate.SkipLabels);

    /// <inheritdoc />
    public DnsName Create(DnsNameSuffix alternate) => alternate.Name.Suffix(alternate.SkipLabels);
}
