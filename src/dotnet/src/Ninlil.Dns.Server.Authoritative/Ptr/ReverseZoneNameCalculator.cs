using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This record carries the names <see cref="PtrSyncService" /> needs for a single A or AAAA
///     record's address: the enclosing reverse zone's own name (the zone this feature auto-creates
///     or reuses), the specific PTR record's own owner name within it, and -- only for an IPv4
///     address grouped at a non-octet-aligned ("classless") prefix length -- the RFC 2317 §4
///     delegating parent zone's own name/owner name pair that must carry a CNAME pointing at
///     <see cref="OwnerName" />, so an ordinary client walking the tree via the conventional,
///     octet-aligned parent <c>/24</c> zone still resolves the right answer. Both delegation
///     fields are <see langword="null" /> for an IPv6 address, and for an IPv4 address grouped at
///     the conventional <c>/24</c> boundary or coarser (no delegation glue is needed when the
///     reverse zone this feature manages already *is* the octet-aligned parent).
/// </summary>
/// <param name="ZoneName">The enclosing reverse zone's own name (without a trailing dot).</param>
/// <param name="OwnerName">The PTR record's own owner name within <see cref="ZoneName" /> (without a trailing dot).</param>
/// <param name="DelegationParentZoneName">
///     The octet-aligned <c>/24</c> parent zone's own name that must carry the RFC 2317 §4
///     delegating CNAME, or <see langword="null" /> when no delegation is needed.
/// </param>
/// <param name="DelegationParentOwnerName">
///     The delegating CNAME's own owner name within <see cref="DelegationParentZoneName" />, or
///     <see langword="null" /> when no delegation is needed. Its target is always
///     <see cref="OwnerName" />.
/// </param>
public sealed record ReverseZoneComputation(
    string ZoneName,
    string OwnerName,
    string DelegationParentZoneName,
    string DelegationParentOwnerName
)
{
    /// <summary>
    ///     This method deconstructs just the reverse zone's own name and owner name, matching the
    ///     shape every call site that predates RFC 2317 classless delegation support already
    ///     expects -- a caller that has no need for the delegation glue fields never has to
    ///     mention them.
    /// </summary>
    public void Deconstruct(out string zoneName, out string ownerName) =>
        (zoneName, ownerName) = (ZoneName, OwnerName);
}

/// <summary>
///     This class computes the reverse-zone/owner-name pair (and, for RFC 2317 classless IPv4
///     delegation, the delegating parent zone's own name/owner name pair too)
///     <see cref="PtrSyncService" /> needs for a single A or AAAA record's address. Neither name
///     previously had a shared helper anywhere in this codebase -- every prior consumer of
///     <c>in-addr.arpa</c>/<c>ip6.arpa</c> is the wire-format <see cref="Common.Wire.Records.PtrRecordData" />
///     codec itself, which only ever reads/writes an already-formed name, never computes one from
///     an address.
/// </summary>
public static class ReverseZoneNameCalculator
{
    /// <summary>
    ///     This constant contains this project's own compiled-in default IPv4 reverse-zone
    ///     grouping granularity -- the conventional octet (<c>/24</c>) boundary -- used whenever
    ///     neither a zone's own <c>ZoneEntity.PtrIpv4PrefixLength</c> override nor
    ///     <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c> supplies one explicitly (a design
    ///     convenience for this class's own test suite and any other caller with no options
    ///     instance in hand; <see cref="PtrSyncService" /> itself always resolves and passes a real
    ///     value, never relying on this default).
    /// </summary>
    public const int DefaultIpv4ReverseZonePrefixBits = 24;

    /// <summary>
    ///     This constant contains the number of bits of an IPv6 address this feature groups a
    ///     reverse zone at, by default -- the nibble boundary chosen for the enclosing
    ///     <c>ip6.arpa</c> zone. <c>/64</c> was chosen deliberately, not left to guesswork: it is
    ///     both a nibble boundary (a hard RFC 3596 requirement -- <c>ip6.arpa</c> labels are always
    ///     one hex nibble each, so only a multiple-of-4 bit count is even expressible) and the
    ///     conventional prefix size a home ISP delegates to a single subscriber's own network (this
    ///     project's primary homelab/home-router deployment target) -- matching IPv4's own <c>/24</c> "one enclosing octet group"
    ///     choice at the equivalent, most-common-real-world granularity for that address family.
    ///     Now a real, configurable setting (<c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>,
    ///     overridable per zone via <c>ZoneEntity.PtrIpv6PrefixLength</c>) rather than a hardcoded
    ///     constant -- this remains only the compiled-in fallback both of those ultimately default
    ///     to.
    /// </summary>
    public const int DefaultIpv6ReverseZonePrefixBits = 64;

    /// <summary>
    ///     This method computes the reverse zone name, PTR owner name, and (for a classless IPv4
    ///     grouping) RFC 2317 §4 delegation glue for a single IPv4 or IPv6 address.
    /// </summary>
    /// <param name="address">The address to compute names for.</param>
    /// <param name="ipv4PrefixBits">
    ///     The IPv4 reverse-zone grouping granularity to use, resolved by the caller from a zone's
    ///     own override or the global default. A value of <c>24</c> (the conventional octet
    ///     boundary) reproduces this feature's original, pre-RFC-2317 behavior exactly; a value
    ///     from <c>25</c> through <c>30</c> groups the reverse zone at that classless boundary
    ///     instead, per RFC 2317's own "<c>/</c>"-delimited naming convention, and also populates
    ///     this address's own delegation glue. Ignored for an IPv6 address.
    /// </param>
    /// <param name="ipv6PrefixBits">
    ///     The IPv6 reverse-zone grouping granularity to use (a multiple of 4, since every
    ///     <c>ip6.arpa</c> label is one hex nibble), resolved by the caller from a zone's own
    ///     override or the global default. Ignored for an IPv4 address.
    /// </param>
    /// <returns>
    ///     The computed <see cref="ReverseZoneComputation" />, deconstructible as just
    ///     <c>(ZoneName, OwnerName)</c> when the caller has no need for the delegation glue fields.
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
    ///     This method attempts to parse a PTR owner name previously computed by
    ///     <see cref="ComputeIpv4" /> at a classless (<c>&gt;24</c>-bit) grouping back into its own
    ///     RFC 2317 §4 delegating parent zone's own name and owner name -- the inverse of
    ///     <see cref="ComputeIpv4" />'s own delegation-glue computation, used by
    ///     <see cref="PtrSyncService" /> to find and clean up a classless PTR record's own
    ///     delegating CNAME once that PTR record itself is no longer desired, without needing the
    ///     original address (already gone, in the removal case) or the prefix length that produced
    ///     it (this parse works for any classless prefix, since the <c>"start/prefix"</c> label is
    ///     self-describing).
    /// </summary>
    /// <param name="classlessOwnerName">A PTR owner name to parse, without a trailing dot.</param>
    /// <param name="parentZoneName">The delegating parent zone's own name, if parsed successfully.</param>
    /// <param name="parentOwnerName">The delegating CNAME's own owner name, if parsed successfully.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="classlessOwnerName" /> is shaped like a
    ///     classless PTR owner name (its second label contains a <c>/</c>), <see langword="false" />
    ///     otherwise.
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

        // A classless owner name is always "{hostOctet}.{startOctet}/{prefixBits}.{c}.{b}.{a}.
        // in-addr.arpa" -- six labels at minimum (host, start/prefix, three octets, "in-addr",
        // "arpa"), with the second label the one and only one containing a "/".
        if (labels.Length < 6 || !labels[1].Contains('/'))
            return false;

        parentZoneName = string.Join('.', labels[2..]);
        parentOwnerName = $"{labels[0]}.{parentZoneName}";
        return true;
    }

    /// <summary>
    ///     This method computes the IPv4 <c>in-addr.arpa</c> names. At the conventional octet
    ///     (<c>&lt;=24</c>-bit) boundary this reproduces the feature's original behavior exactly,
    ///     with no delegation glue. At a classless (<c>25</c>-through-<c>30</c>-bit) boundary, the
    ///     reverse zone is instead named per RFC 2317 §4's own <c>"{start}/{prefix}"</c> convention
    ///     (e.g. <c>0/25.2.0.192.in-addr.arpa</c> for the lower half of <c>192.0.2.0/24</c>), and
    ///     the octet-aligned <c>/24</c> parent zone's own corresponding delegation glue
    ///     (a CNAME pointing at this classless owner name) is also computed, since RFC 2317 is a
    ///     mechanism for delegating *within* the conventional tree, not a replacement for it -- an
    ///     ordinary client resolving a PTR query still starts at the same <c>/24</c> parent zone
    ///     any non-classless deployment would use, and is redirected into the classless child zone
    ///     only via that CNAME.
    /// </summary>
    private static ReverseZoneComputation ComputeIpv4(IPAddress address, int prefixBits)
    {
        byte[] octets = address.GetAddressBytes();

        // The octet-aligned /24 parent is always computable, regardless of prefixBits -- the
        // address's own first three octets, written in reverse order -- and is either the reverse
        // zone itself (prefixBits <= 24) or the RFC 2317 delegating parent (prefixBits > 24).
        string parentZoneName = $"{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";
        string parentOwnerName = $"{octets[3]}.{parentZoneName}";

        if (prefixBits <= 24)
            return new ReverseZoneComputation(parentZoneName, parentOwnerName, null, null);

        // Classless grouping: every host bit beyond the /24 boundary lives entirely within the
        // address's own fourth octet (prefixBits is always > 24 here), so the block's own start
        // octet is that octet masked down to its own block boundary.
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
    ///     This method computes the IPv6 <c>ip6.arpa</c> names -- the enclosing zone at the
    ///     nibble boundary <paramref name="prefixBits" /> names. IPv6 has no analogue to RFC 2317
    ///     classless IPv4 delegation (its address space is never this scarce), so no delegation
    ///     glue is ever computed here.
    /// </summary>
    private static ReverseZoneComputation ComputeIpv6(IPAddress address, int prefixBits)
    {
        byte[] bytes = address.GetAddressBytes();
        int nibbleCount = prefixBits / 4;

        // Every ip6.arpa label is exactly one hex nibble, written most-significant-nibble-first
        // in the address itself, then reversed entirely for presentation (RFC 3596 §2.5).
        char[] nibblesMostSignificantFirst = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            nibblesMostSignificantFirst[i * 2] = ToHexNibble((byte)(bytes[i] >> 4));
            nibblesMostSignificantFirst[i * 2 + 1] = ToHexNibble((byte)(bytes[i] & 0x0F));
        }

        char[] reversedNibbles = [.. nibblesMostSignificantFirst.Reverse()];

        string ownerName = $"{string.Join('.', reversedNibbles)}.ip6.arpa";

        // The enclosing zone's own name is the same reversed sequence, but only the nibbles
        // covering the prefix -- which, since the sequence is already reversed
        // (least-significant nibble first), means skipping the first (32 - nibbleCount) entries
        // (the host portion) rather than taking the first nibbleCount of the un-reversed sequence.
        string zoneName =
            $"{string.Join('.', reversedNibbles.Skip(reversedNibbles.Length - nibbleCount))}.ip6.arpa";

        return new ReverseZoneComputation(zoneName, ownerName, null, null);
    }

    /// <summary>
    ///     This method converts a 4-bit value into its lowercase hexadecimal character.
    /// </summary>
    private static char ToHexNibble(byte nibble) => "0123456789abcdef"[nibble];
}
