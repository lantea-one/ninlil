// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class hands out address ranges that no other test uses. The shared host keeps every
///     scope a test creates, and scope writes refuse a range that overlaps another enabled scope
///     of the same family, so each test that needs a scope takes a fresh /24 (IPv4) or /64
///     (IPv6) from here instead of repeating a fixed range. The numbering restarts with each
///     test process, and the ranges lie in <c>10.200.0.0/16</c> and <c>fd00:200::/32</c>, away
///     from the fixed ranges the older tests use.
/// </summary>
public static class DhcpTestScopeRanges
{
    private static int _next;

    /// <summary>
    ///     This method returns a new IPv4 /24 as its first usable host address, its last host
    ///     address and the prefix length.
    /// </summary>
    public static (string Start, string End, int PrefixLength) NextIPv4()
    {
        int index = Interlocked.Increment(ref _next) % 256;
        return ($"10.200.{index}.10", $"10.200.{index}.200", 24);
    }

    /// <summary>
    ///     This method returns a new IPv6 /64 as its first address, its last address and the
    ///     prefix length.
    /// </summary>
    public static (string Start, string End, int PrefixLength) NextIPv6()
    {
        int index = Interlocked.Increment(ref _next);
        return ($"fd00:200:{index:x}::10", $"fd00:200:{index:x}::ff", 64);
    }
}
