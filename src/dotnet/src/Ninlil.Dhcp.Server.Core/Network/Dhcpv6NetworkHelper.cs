using System.Net;
using System.Net.Sockets;
using System.Numerics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Network;

/// <summary>
///     This static class provides the IPv6 address-arithmetic primitives the DHCPv6 engine
///     needs -- the IPv6 analogue of <see cref="Dhcpv4NetworkHelper" />, using
///     <see cref="BigInteger" /> rather than a built-in integer type since a 128-bit address does
///     not fit in any of them.
///     <para>
///         <b>Why a bounded pool scan, unlike <see cref="Dhcpv4NetworkHelper.Enumerate" /></b>:
///         a DHCPv4 pool is, in practice, at most a handful of thousand addresses (a <c>/16</c> at
///         the very largest a real deployment would configure), cheap to scan address by address.
///         A DHCPv6 pool is routinely astronomically larger -- even a single, ordinary <c>/64</c>
///         holds 2^64 addresses -- so a literal sequential scan across the full configured range
///         is not a viable allocation strategy at all; the engine that calls
///         <see cref="EnumerateAddresses" />/<see cref="EnumeratePrefixes" /> therefore only ever
///         asks for a first <see cref="MaximumPoolScanCandidates" />-sized slice starting from the
///         range's own start, which is checked against this project's own actually-recorded lease
///         rows (a real homelab deployment holds, realistically, a few dozen to a few thousand
///         leases at once) rather than the pool's own theoretical size. A pool whose first
///         <see cref="MaximumPoolScanCandidates" /> candidates are all somehow already occupied
///         reports no address available rather than continuing to scan deeper into an
///         effectively-infinite space -- a deliberate, honest scope limit for this engine's own
///         sequential-scan allocation strategy, not a claim that a sparse, huge pool always finds
///         a free address if a later one genuinely exists.
///     </para>
/// </summary>
internal static class Dhcpv6NetworkHelper
{
    /// <summary>
    ///     This constant bounds how many candidates <see cref="EnumerateAddresses" />/
    ///     <see cref="EnumeratePrefixes" /> ever yield -- see this class's own doc comment for why.
    /// </summary>
    public const int MaximumPoolScanCandidates = 65536;

    /// <summary>
    ///     This method converts an IPv6 <see cref="IPAddress" /> into its unsigned, 128-bit
    ///     integer form, letting a range of addresses/prefixes be enumerated and compared
    ///     numerically.
    /// </summary>
    /// <param name="address">The IPv6 address to convert.</param>
    /// <returns>The address's own unsigned 128-bit integer form.</returns>
    public static BigInteger ToBigInteger(IPAddress address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Must be an IPv6 address.", nameof(address));

        // BigInteger's own byte-array constructor reads little-endian, with the final byte's own
        // top bit as the sign -- an ordinary IPv6 address is big-endian on the wire and, with its
        // own top bit potentially set, would otherwise be misread as negative, so the 16 address
        // bytes are reversed and a trailing zero byte is appended to force an unsigned read.
        byte[] bytes = address.GetAddressBytes();
        Array.Reverse(bytes);
        return new BigInteger([.. bytes, 0]);
    }

    /// <summary>
    ///     This method converts an unsigned 128-bit integer back into an IPv6
    ///     <see cref="IPAddress" />, the inverse of <see cref="ToBigInteger" />.
    /// </summary>
    /// <param name="value">The address's own unsigned 128-bit integer form.</param>
    /// <returns>The equivalent <see cref="IPAddress" />.</returns>
    public static IPAddress FromBigInteger(BigInteger value)
    {
        // BigInteger.ToByteArray() returns the fewest bytes needed, little-endian, and may omit
        // leading (high-order, once reversed) zero bytes entirely -- pad up to the full 16 bytes
        // before reversing back to network byte order.
        byte[] bytes = value.ToByteArray(isUnsigned: true);
        byte[] result = new byte[16];
        bytes.AsSpan(0, Math.Min(bytes.Length, 16)).CopyTo(result);
        Array.Reverse(result);
        return new IPAddress(result);
    }

    /// <summary>
    ///     This method reports whether two IPv6 addresses fall within the same network, per a
    ///     given CIDR prefix length -- both addresses' leading <paramref name="prefixLength" />
    ///     bits must be identical. The IPv6 analogue of <see cref="Dhcpv4NetworkHelper.AreInSameNetwork" />.
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
    ///     This method enumerates individual addresses in an inclusive
    ///     [<paramref name="rangeStart" />, <paramref name="rangeEnd" />] range, in ascending
    ///     order, up to <see cref="MaximumPoolScanCandidates" /> of them -- used by a
    ///     <c>DhcpScopeKindEnum.AddressAssignment</c> scope's own pool scan.
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
    ///     This method enumerates block-aligned prefix candidates, each exactly
    ///     <paramref name="delegatedPrefixLength" /> bits long, within an inclusive
    ///     [<paramref name="rangeStart" />, <paramref name="rangeEnd" />] range, in ascending
    ///     order, up to <see cref="MaximumPoolScanCandidates" /> of them -- used by a
    ///     <c>DhcpScopeKindEnum.PrefixDelegation</c> scope's own pool scan. The first candidate is
    ///     <paramref name="rangeStart" /> itself, block-aligned downward to a multiple of the
    ///     block size, so a pool range that does not itself start on a block boundary still yields
    ///     a sensible, deterministic first candidate rather than skipping straight to the next
    ///     boundary.
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
    ///     This method computes the 128-bit network mask for a CIDR prefix length -- for example,
    ///     <c>64</c> yields the top 64 bits set, the rest clear. Kept <see langword="internal" />
    ///     (rather than <see langword="private" />) so <see cref="Statistics.DhcpScopeStatisticsService" />
    ///     can reuse the exact same block-alignment arithmetic <see cref="EnumeratePrefixes" />
    ///     already establishes, rather than a second, duplicated copy of this formula.
    /// </summary>
    internal static BigInteger ComputeMask(int prefixLength)
    {
        BigInteger fullMask = (BigInteger.One << 128) - 1;
        return prefixLength == 128
            ? fullMask
            : fullMask ^ ((BigInteger.One << (128 - prefixLength)) - 1);
    }
}
