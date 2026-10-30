using System.Net;
using System.Net.Sockets;
using System.Numerics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Network;

/// <summary>
///     This static class provides the IPv6 address arithmetic of the DHCPv6 engine, the
///     counterpart of <see cref="Dhcpv4NetworkHelper" />, using <see cref="BigInteger" /> because
///     a 128-bit address fits no built-in integer type.
///     <para>
///         <b>The pool scan is bounded</b>, unlike <see cref="Dhcpv4NetworkHelper.Enumerate" />.
///         A DHCPv4 pool has at most tens of thousands of addresses (a <c>/16</c> has 65,536),
///         but a single IPv6 <c>/64</c> has 2^64, so scanning the whole range is impossible.
///         <see cref="EnumerateAddresses" /> and <see cref="EnumeratePrefixes" /> return at most
///         the first <see cref="MaximumPoolScanCandidates" /> candidates from the start of the
///         range, which the allocation service checks against the recorded leases. If all of
///         them are taken, no address is offered even though later ones may be free.
///     </para>
/// </summary>
internal static class Dhcpv6NetworkHelper
{
    /// <summary>
    ///     This constant bounds how many candidates <see cref="EnumerateAddresses" /> and
    ///     <see cref="EnumeratePrefixes" /> return (see the class summary).
    /// </summary>
    public const int MaximumPoolScanCandidates = 65536;

    /// <summary>
    ///     This method converts an IPv6 address into its unsigned 128-bit integer form. It throws
    ///     for a null or non-IPv6 address.
    /// </summary>
    /// <param name="address">The IPv6 address to convert.</param>
    /// <returns>The address as an unsigned 128-bit integer.</returns>
    public static BigInteger ToBigInteger(IPAddress address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Must be an IPv6 address.", nameof(address));

        // The BigInteger byte-array constructor reads little-endian and takes the sign from the
        // top bit of the last byte. Reverse the big-endian address bytes and append a zero byte
        // so an address with its top bit set is not read as negative.
        byte[] bytes = address.GetAddressBytes();
        Array.Reverse(bytes);
        return new BigInteger([.. bytes, 0]);
    }

    /// <summary>
    ///     This method converts an unsigned 128-bit integer back into an IPv6
    ///     <see cref="IPAddress" />, the inverse of <see cref="ToBigInteger" />.
    /// </summary>
    /// <param name="value">The address as an unsigned 128-bit integer.</param>
    /// <returns>The equivalent <see cref="IPAddress" />.</returns>
    public static IPAddress FromBigInteger(BigInteger value)
    {
        // ToByteArray returns the fewest little-endian bytes, omitting high-order zero bytes, so
        // pad to 16 bytes before reversing to network byte order. Bits above 128 are dropped.
        byte[] bytes = value.ToByteArray(isUnsigned: true);
        byte[] result = new byte[16];
        bytes.AsSpan(0, Math.Min(bytes.Length, 16)).CopyTo(result);
        Array.Reverse(result);
        return new IPAddress(result);
    }

    /// <summary>
    ///     This method reports whether two IPv6 addresses share their leading
    ///     <paramref name="prefixLength" /> bits, like <see cref="Dhcpv4NetworkHelper.AreInSameNetwork" />.
    ///     It throws for a prefix length outside 0-128.
    /// </summary>
    /// <param name="first">The first address to compare.</param>
    /// <param name="second">The second address to compare.</param>
    /// <param name="prefixLength">The CIDR prefix length, in bits (0-128), to compare over.</param>
    /// <returns>
    ///     <see langword="true" /> if both addresses share the same leading
    ///     <paramref name="prefixLength" /> bits; <see langword="false" /> otherwise.
    /// </returns>
    public static bool AreInSameNetwork(IPAddress first, IPAddress second, int prefixLength)
    {
        if (prefixLength is < 0 or > 128)
            throw new ArgumentOutOfRangeException(
                nameof(prefixLength),
                prefixLength,
                "A CIDR prefix length must be between 0 and 128, inclusive."
            );

        if (prefixLength == 0)
            return true;

        BigInteger mask = ComputeMask(prefixLength);
        return (ToBigInteger(first) & mask) == (ToBigInteger(second) & mask);
    }

    /// <summary>
    ///     This method reports whether an address falls within an inclusive
    ///     [<paramref name="rangeStart" />, <paramref name="rangeEnd" />] range.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <param name="rangeStart">The first address in the range, inclusive.</param>
    /// <param name="rangeEnd">The last address in the range, inclusive.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="address" /> falls within the range;
    ///     <see langword="false" /> otherwise.
    /// </returns>
    public static bool IsWithinRange(IPAddress address, IPAddress rangeStart, IPAddress rangeEnd)
    {
        BigInteger value = ToBigInteger(address);
        return value >= ToBigInteger(rangeStart) && value <= ToBigInteger(rangeEnd);
    }

    /// <summary>
    ///     This method enumerates addresses in the inclusive range [<paramref name="rangeStart" />,
    ///     <paramref name="rangeEnd" />] in ascending order, at most
    ///     <see cref="MaximumPoolScanCandidates" />, for the pool scan of an
    ///     <c>DhcpScopeKindEnum.AddressAssignment</c> scope.
    /// </summary>
    /// <param name="rangeStart">The first address in the range, inclusive.</param>
    /// <param name="rangeEnd">The last address in the range, inclusive.</param>
    /// <returns>Up to <see cref="MaximumPoolScanCandidates" /> addresses in the range, ascending.</returns>
    public static IEnumerable<IPAddress> EnumerateAddresses(
        IPAddress rangeStart,
        IPAddress rangeEnd
    )
    {
        BigInteger start = ToBigInteger(rangeStart);
        BigInteger end = ToBigInteger(rangeEnd);

        for (
            (BigInteger value, int count) = (start, 0);
            value <= end && count < MaximumPoolScanCandidates;
            value++, count++
        )
            yield return FromBigInteger(value);
    }

    /// <summary>
    ///     This method enumerates prefixes of <paramref name="delegatedPrefixLength" /> bits in
    ///     ascending order, at most <see cref="MaximumPoolScanCandidates" />, for the pool scan of
    ///     a <c>DhcpScopeKindEnum.PrefixDelegation</c> scope. The first candidate is
    ///     <paramref name="rangeStart" /> rounded down to a prefix boundary, so a range that does
    ///     not start on a boundary begins with the prefix containing its start (which may extend
    ///     below the start). It throws for a prefix length outside 1-128.
    /// </summary>
    /// <param name="rangeStart">The first address of the delegable pool, inclusive.</param>
    /// <param name="rangeEnd">The last address of the delegable pool, inclusive.</param>
    /// <param name="delegatedPrefixLength">The length, in bits, of each delegated prefix block.</param>
    /// <returns>
    ///     Up to <see cref="MaximumPoolScanCandidates" /> block-aligned prefix base addresses in
    ///     the range, ascending.
    /// </returns>
    public static IEnumerable<IPAddress> EnumeratePrefixes(
        IPAddress rangeStart,
        IPAddress rangeEnd,
        int delegatedPrefixLength
    )
    {
        if (delegatedPrefixLength is < 1 or > 128)
            throw new ArgumentOutOfRangeException(
                nameof(delegatedPrefixLength),
                delegatedPrefixLength,
                "A delegated prefix length must be between 1 and 128, inclusive."
            );

        BigInteger blockSize = BigInteger.One << (128 - delegatedPrefixLength);
        BigInteger mask = ComputeMask(delegatedPrefixLength);
        BigInteger start = ToBigInteger(rangeStart) & mask;
        BigInteger end = ToBigInteger(rangeEnd);

        for (
            (BigInteger value, int count) = (start, 0);
            value <= end && count < MaximumPoolScanCandidates;
            value += blockSize, count++
        )
            yield return FromBigInteger(value);
    }

    /// <summary>
    ///     This method reports whether a prefix is aligned: none of the bits beyond
    ///     <paramref name="prefixLength" /> are set, so the address is the base of its block. A
    ///     delegated prefix must be aligned: RFC 8415 §21.22 carries a prefix and its length, and
    ///     Ninlil accepts only a base address.
    /// </summary>
    /// <param name="prefix">The prefix base address to check.</param>
    /// <param name="prefixLength">The prefix length, in bits (0-128).</param>
    /// <returns><see langword="true" /> when the address has no host bits set.</returns>
    public static bool IsAlignedPrefix(IPAddress prefix, int prefixLength)
    {
        if (prefixLength is < 0 or > 128)
            throw new ArgumentOutOfRangeException(
                nameof(prefixLength),
                prefixLength,
                "A prefix length must be between 0 and 128, inclusive."
            );

        return (ToBigInteger(prefix) & ComputeMask(prefixLength)) == ToBigInteger(prefix);
    }

    /// <summary>
    ///     This method reports whether two prefixes overlap: both, cut to the shorter of the two
    ///     lengths, are the same block. Equal and nested prefixes overlap; disjoint ones do not.
    /// </summary>
    /// <param name="first">The first prefix address.</param>
    /// <param name="firstLength">The first prefix length, in bits (0-128).</param>
    /// <param name="second">The second prefix address.</param>
    /// <param name="secondLength">The second prefix length, in bits (0-128).</param>
    /// <returns><see langword="true" /> when the two prefixes share any address.</returns>
    public static bool PrefixesOverlap(
        IPAddress first,
        int firstLength,
        IPAddress second,
        int secondLength
    ) => AreInSameNetwork(first, second, Math.Min(firstLength, secondLength));

    /// <summary>
    ///     This method computes the 128-bit mask for a prefix length (for <c>64</c>, the top 64
    ///     bits set). <see cref="Statistics.DhcpScopeStatisticsService" /> also uses it, so its
    ///     prefix count rounds the same way as <see cref="EnumeratePrefixes" />.
    /// </summary>
    internal static BigInteger ComputeMask(int prefixLength)
    {
        BigInteger fullMask = (BigInteger.One << 128) - 1;
        return prefixLength == 128
            ? fullMask
            : fullMask ^ ((BigInteger.One << (128 - prefixLength)) - 1);
    }
}
