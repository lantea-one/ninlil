using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This record carries the names <see cref="PtrSyncService" /> needs for one A or AAAA
///     address: the reverse zone, the PTR owner name in it and, for IPv4 grouped at a classless
///     prefix (25 to 30), the enclosing /24 zone and owner name that must carry an RFC 2317 §4
///     CNAME to <see cref="OwnerName" />, so clients walking the normal tree find the PTR. The
///     delegation fields are <see langword="null" /> for IPv6 and for IPv4 at /24.
/// </summary>
/// <param name="ZoneName">The reverse zone's name, without a trailing dot.</param>
/// <param name="OwnerName">The PTR record's owner name, without a trailing dot.</param>
/// <param name="DelegationParentZoneName">
///     The enclosing /24 zone that carries the RFC 2317 §4 CNAME, or <see langword="null" /> when
///     none is needed.
/// </param>
/// <param name="DelegationParentOwnerName">
///     The CNAME's owner name in <see cref="DelegationParentZoneName" />, or
///     <see langword="null" /> when none is needed. Its target is <see cref="OwnerName" />.
/// </param>
public sealed record ReverseZoneComputation(
    string ZoneName,
    string OwnerName,
    string DelegationParentZoneName,
    string DelegationParentOwnerName
)
{
    /// <summary>
    ///     This method deconstructs only the zone name and owner name, for callers that do not need
    ///     the delegation fields.
    /// </summary>
    public void Deconstruct(out string zoneName, out string ownerName) =>
        (zoneName, ownerName) = (ZoneName, OwnerName);
}

/// <summary>
///     This class computes the reverse zone name and PTR owner name (and, for RFC 2317 classless
///     IPv4 zones, the enclosing zone's CNAME name) that <see cref="PtrSyncService" /> needs for an
///     A or AAAA address.
/// </summary>
public static class ReverseZoneNameCalculator
{
    /// <summary>
    ///     This constant contains the fallback IPv4 prefix length, 24, for callers without options
    ///     (mainly tests). <see cref="PtrSyncService" /> always passes a resolved value.
    /// </summary>
    public const int DefaultIpv4ReverseZonePrefixBits = 24;

    /// <summary>
    ///     This constant contains the fallback IPv6 prefix length, 64, behind
    ///     <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c> and
    ///     <c>ZoneEntity.PtrIpv6PrefixLength</c>. It is a nibble boundary (RFC 3596 labels are one hex
    ///     digit each) and the usual size of a home network, the IPv6 counterpart of an IPv4 /24.
    /// </summary>
    public const int DefaultIpv6ReverseZonePrefixBits = 64;

    /// <summary>
    ///     This method computes the reverse zone name, PTR owner name and, for a classless IPv4
    ///     prefix, the RFC 2317 §4 CNAME names for one address.
    /// </summary>
    /// <param name="address">The address to compute names for.</param>
    /// <param name="ipv4PrefixBits">
    ///     The IPv4 prefix length, resolved by the caller from the zone or the global default. 24
    ///     gives one zone per /24. 25 to 30 names the zone with the RFC 2317 <c>"{start}/{prefix}"</c>
    ///     form and fills the delegation fields. Ignored for IPv6.
    /// </param>
    /// <param name="ipv6PrefixBits">
    ///     The IPv6 prefix length (a multiple of 4, since each <c>ip6.arpa</c> label is a nibble),
    ///     resolved by the caller. Ignored for IPv4.
    /// </param>
    /// <returns>
    ///     The <see cref="ReverseZoneComputation" />, which can be deconstructed as
    ///     <c>(ZoneName, OwnerName)</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="address" /> is neither IPv4 nor IPv6.
    /// </exception>
    public static ReverseZoneComputation Compute(
        IPAddress address,
        int ipv4PrefixBits = DefaultIpv4ReverseZonePrefixBits,
        int ipv6PrefixBits = DefaultIpv6ReverseZonePrefixBits
    ) =>
        address.AddressFamily switch
        {
            AddressFamily.InterNetwork => ComputeIpv4(address, ipv4PrefixBits),
            AddressFamily.InterNetworkV6 => ComputeIpv6(address, ipv6PrefixBits),
            _ => throw new ArgumentException(
                $"'{address}' is neither an IPv4 nor an IPv6 address.",
                nameof(address)
            ),
        };

    /// <summary>
    ///     This method parses a classless PTR owner name from <see cref="ComputeIpv4" /> into the
    ///     enclosing /24 zone and CNAME owner name. <see cref="PtrSyncService" /> uses it to remove
    ///     the CNAME when the PTR goes away, when the address and prefix are no longer known; the
    ///     <c>"start/prefix"</c> label describes itself.
    /// </summary>
    /// <param name="classlessOwnerName">A PTR owner name to parse, without a trailing dot.</param>
    /// <param name="parentZoneName">The enclosing /24 zone's name, if parsed.</param>
    /// <param name="parentOwnerName">The CNAME's owner name, if parsed.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="classlessOwnerName" /> has the classless shape
    ///     (its second label contains <c>/</c>); otherwise <see langword="false" />.
    /// </returns>
    public static bool TryGetClasslessDelegationParent(
        string classlessOwnerName,
        out string parentZoneName,
        out string parentOwnerName
    )
    {
        parentZoneName = null;
        parentOwnerName = null;

        string[] labels = classlessOwnerName.TrimEnd('.').Split('.');

        // A classless owner name is "{host}.{start}/{prefix}.{c}.{b}.{a}.in-addr.arpa": at least
        // seven labels, and only the second contains "/".
        if (labels.Length < 6 || !labels[1].Contains('/'))
            return false;

        parentZoneName = string.Join('.', labels[2..]);
        parentOwnerName = $"{labels[0]}.{parentZoneName}";
        return true;
    }

    /// <summary>
    ///     This method computes the IPv4 <c>in-addr.arpa</c> names. At 24 bits or fewer the zone is
    ///     the /24 zone and there is no delegation. At 25 to 30 bits the zone is named with the RFC
    ///     2317 §4 <c>"{start}/{prefix}"</c> form (for example <c>0/25.2.0.192.in-addr.arpa</c> for the
    ///     lower half of <c>192.0.2.0/24</c>), and the /24 zone gets a CNAME to it, since clients still
    ///     start at the /24 zone.
    /// </summary>
    private static ReverseZoneComputation ComputeIpv4(IPAddress address, int prefixBits)
    {
        byte[] octets = address.GetAddressBytes();

        // The /24 zone (the first three octets reversed) is either the reverse zone itself or the
        // RFC 2317 parent.
        string parentZoneName = $"{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";
        string parentOwnerName = $"{octets[3]}.{parentZoneName}";

        if (prefixBits <= 24)
            return new ReverseZoneComputation(parentZoneName, parentOwnerName, null, null);

        // Classless: the prefix ends in the fourth octet, so the block starts at that octet masked
        // to the block size.
        int hostBitsInLastOctet = 32 - prefixBits;
        int blockSize = 1 << hostBitsInLastOctet;
        int startOctet = octets[3] - (octets[3] % blockSize);

        string childZoneName = $"{startOctet}/{prefixBits}.{parentZoneName}";
        string childOwnerName = $"{octets[3]}.{childZoneName}";

        return new ReverseZoneComputation(
            childZoneName,
            childOwnerName,
            parentZoneName,
            parentOwnerName
        );
    }

    /// <summary>
    ///     This method computes the IPv6 <c>ip6.arpa</c> names, with the zone at
    ///     <paramref name="prefixBits" />. There is no IPv6 counterpart to RFC 2317, so no delegation
    ///     names are computed.
    /// </summary>
    private static ReverseZoneComputation ComputeIpv6(IPAddress address, int prefixBits)
    {
        byte[] bytes = address.GetAddressBytes();
        int nibbleCount = prefixBits / 4;

        // Each ip6.arpa label is one hex nibble, in reverse order (RFC 3596 §2.5).
        char[] nibblesMostSignificantFirst = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            nibblesMostSignificantFirst[i * 2] = ToHexNibble((byte)(bytes[i] >> 4));
            nibblesMostSignificantFirst[i * 2 + 1] = ToHexNibble((byte)(bytes[i] & 0x0F));
        }

        char[] reversedNibbles = [.. nibblesMostSignificantFirst.Reverse()];

        string ownerName = $"{string.Join('.', reversedNibbles)}.ip6.arpa";

        // The zone name is the same reversed list without the host nibbles, which come first in
        // reversed order: skip the first (32 - nibbleCount) entries.
        string zoneName =
            $"{string.Join('.', reversedNibbles.Skip(reversedNibbles.Length - nibbleCount))}.ip6.arpa";

        return new ReverseZoneComputation(zoneName, ownerName, null, null);
    }

    /// <summary>
    ///     This method converts a 4-bit value into its lowercase hexadecimal character.
    /// </summary>
    private static char ToHexNibble(byte nibble) => "0123456789abcdef"[nibble];
}
