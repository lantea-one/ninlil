using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="SoaSerialArithmetic" /> and
///     <see cref="SecondaryZoneRefreshService.IsPrimaryAhead" /> against the RFC 1982 §3.2 rules,
///     including a wrapped serial and the undefined pair 2^31 apart. They are pure functions, so
///     the tests need no host.
/// </summary>
public class SoaSerialArithmeticTests
{
    [Theory]
    [InlineData(1u, 0u, true)]
    [InlineData(0u, 4294967295u, true)]
    [InlineData(4294967295u, 0u, false)]
    [InlineData(5u, 5u, false)]
    [InlineData(2147483648u, 0u, false)]
    [InlineData(0u, 2147483648u, false)]
    [InlineData(2147483647u, 0u, true)]
    [InlineData(2147483649u, 1u, false)]
    public void IsGreaterThan_ASerialPair_FollowsRfc1982(uint s1, uint s2, bool expected) =>
        Assert.Equal(expected, SoaSerialArithmetic.IsGreaterThan(s1, s2));

    [Fact]
    public void IsPrimaryAhead_APrimaryThatWrapped_IsAhead() =>
        Assert.True(SecondaryZoneRefreshService.IsPrimaryAhead(4294967295, 1));

    [Fact]
    public void IsPrimaryAhead_APrimaryBehindTheLocalCopy_IsNotAhead() =>
        Assert.False(SecondaryZoneRefreshService.IsPrimaryAhead(1, 4294967295));

    [Fact]
    public void IsPrimaryAhead_TheSameSerial_IsNotAhead() =>
        Assert.False(SecondaryZoneRefreshService.IsPrimaryAhead(7, 7));

    [Fact]
    public void IsPrimaryAhead_AnUndefinedPair_IsNotAhead() =>
        Assert.False(SecondaryZoneRefreshService.IsPrimaryAhead(0, 2147483648));

    [Fact]
    public void IsPrimaryAhead_NoLocalCopy_IsAhead() =>
        Assert.True(SecondaryZoneRefreshService.IsPrimaryAhead(null, 0));
}
