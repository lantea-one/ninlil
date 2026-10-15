// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements RFC 1982 serial number arithmetic for 32-bit SOA serials
///     (SERIAL_BITS = 32, RFC 1982 §2). A serial wraps from 4294967295 to 0, so a plain integer
///     comparison calls a wrapped primary "older" and stops transfers. <see cref="ZoneTransferService" />
///     uses it to decide whether an IXFR client is current, and
///     <see cref="Refresh.SecondaryZoneRefreshService" /> uses it to decide whether a primary is ahead.
/// </summary>
internal static class SoaSerialArithmetic
{
    /// <summary>
    ///     This method reports whether <paramref name="s1" /> is greater than <paramref name="s2" />
    ///     under RFC 1982 §3.2. A pair exactly 2^31 apart is undefined there; it is reported as not
    ///     greater, so neither side starts a transfer on it.
    /// </summary>
    /// <param name="s1">The serial that may be newer.</param>
    /// <param name="s2">The serial it is compared with.</param>
    /// <returns><see langword="true" /> when <paramref name="s1" /> is ahead of <paramref name="s2" />.</returns>
    public static bool IsGreaterThan(uint s1, uint s2)
    {
        // The wrapped difference is the forward distance from s2 to s1. Distances of 1 to 2^31 - 1
        // are "ahead" (RFC 1982 §3.2); 0 is equal and exactly 2^31 is the undefined pair.
        uint distance = unchecked(s1 - s2);
        return distance != 0 && distance < 0x80000000u;
    }
}
