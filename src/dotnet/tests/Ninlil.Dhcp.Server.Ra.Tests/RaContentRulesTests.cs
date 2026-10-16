using System.Net;
using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks every limit in <see cref="RaContentRules" /> at, below and above its
///     bound, and the prefix masking. It is pure logic and needs no host.
/// </summary>
public class RaContentRulesTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(-1, false)]
    public void CheckRouterLifetime_AtAndAroundTheFieldWidth_AcceptsOnlyTheRange(
        int seconds,
        bool valid
    )
    {
        string error = RaContentRules.CheckRouterLifetime(TimeSpan.FromSeconds(seconds));

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void CheckRouterLifetime_AboveTheRange_NamesTheFieldAndTheLimit()
    {
        string error = RaContentRules.CheckRouterLifetime(TimeSpan.FromDays(1));

        Assert.Equal("'routerLifetime' must be at most 18:12:15 (65535 seconds).", error);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3_600_000, true)]
    [InlineData(3_600_001, false)]
    public void CheckReachableTime_AtAndAroundOneHour_AcceptsOnlyTheRange(
        int milliseconds,
        bool valid
    )
    {
        string error = RaContentRules.CheckReachableTime(TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void CheckRetransTimer_AtAndAboveTheFieldWidth_AcceptsOnlyTheRange()
    {
        Assert.Null(RaContentRules.CheckRetransTimer(TimeSpan.FromMilliseconds(uint.MaxValue)));
        Assert.NotNull(
            RaContentRules.CheckRetransTimer(TimeSpan.FromMilliseconds(uint.MaxValue + 1000d))
        );
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1280u, true)]
    [InlineData(1279u, false)]
    [InlineData(1000u, false)]
    public void CheckLinkMtu_AtAndBelowTheIpv6Minimum_AcceptsOnlyTheRange(uint? mtu, bool valid)
    {
        Assert.Equal(valid, RaContentRules.CheckLinkMtu(mtu) is null);
    }

    [Theory]
    [InlineData("2001:db8::53", true)]
    [InlineData("fe80::1", true)]
    [InlineData("192.0.2.1", false)]
    [InlineData("::ffff:192.0.2.1", false)]
    [InlineData("::", false)]
    [InlineData("ff02::1", false)]
    [InlineData("not-an-address", false)]
    public void CheckRecursiveDnsServer_ForEachAddressKind_AcceptsOnlyUsableIpv6(
        string text,
        bool valid
    )
    {
        Assert.Equal(valid, RaContentRules.CheckRecursiveDnsServer(text) is null);
    }

    [Fact]
    public void CheckRecursiveDnsServerCount_AtAndAbove75_AcceptsOnlyTheRange()
    {
        Assert.Null(RaContentRules.CheckRecursiveDnsServerCount(75));
        Assert.NotNull(RaContentRules.CheckRecursiveDnsServerCount(76));
    }

    [Theory]
    [InlineData("a.b", 16)]
    [InlineData(".", 16)]
    [InlineData("homelab.internal", 32)]
    public void EncodedDnsSearchListOctets_MatchesTheOptionTheEncoderWrites(
        string domainName,
        int expected
    )
    {
        RouterAdvertisementMessage message = new();
        message.SetDnsSearchList(TimeSpan.FromMinutes(30), [domainName]);
        RaOption option = Assert.Single(message.Options);

        Assert.Equal(expected, RaContentRules.EncodedDnsSearchListOctets([domainName]));
        Assert.Equal(expected, 2 + option.Data.Length);
    }

    [Fact]
    public void CheckDnsSearchListSize_AtAndAboveTheLimit_AcceptsOnlyTheRange()
    {
        // Each name encodes to 65 octets: 18 give a 1184-octet option, 19 give 1248.
        string[] names = [.. Enumerable.Range(10, 19).Select(i => new string('a', 60) + "." + i)];

        Assert.Null(RaContentRules.CheckDnsSearchListSize(names[..18]));
        Assert.NotNull(RaContentRules.CheckDnsSearchListSize(names));
        Assert.Null(RaContentRules.CheckDnsSearchListSize([]));
    }

    [Theory]
    [InlineData("2001:db8::", 0, true)]
    [InlineData("2001:db8::", 128, true)]
    [InlineData("2001:db8::", 129, false)]
    [InlineData("2001:db8::", -1, false)]
    [InlineData("192.0.2.0", 24, false)]
    [InlineData("fe80::", 64, false)]
    [InlineData("ff02::", 64, false)]
    public void CheckPrefix_ForEachAddressAndLength_AcceptsOnlyAdvertisablePrefixes(
        string text,
        int length,
        bool valid
    )
    {
        Assert.Equal(valid, RaContentRules.CheckPrefix(text, length) is null);
    }

    [Theory]
    [InlineData("fe80::", 10, true)]
    [InlineData(null, 0, true)]
    [InlineData(null, 64, false)]
    [InlineData("192.0.2.0", 24, false)]
    [InlineData("2001:db8::", 129, false)]
    public void CheckRoutePrefix_ForEachAddressAndLength_AllowsLinkLocalAndAddresslessDefault(
        string text,
        int length,
        bool valid
    )
    {
        Assert.Equal(valid, RaContentRules.CheckRoutePrefix(text, length) is null);
    }

    [Fact]
    public void CheckPrefixLifetimes_WithPreferredAboveValid_Rejects()
    {
        Assert.Null(
            RaContentRules.CheckPrefixLifetimes(TimeSpan.FromDays(2), TimeSpan.FromDays(2))
        );
        Assert.NotNull(
            RaContentRules.CheckPrefixLifetimes(TimeSpan.FromDays(1), TimeSpan.FromDays(2))
        );
        Assert.NotNull(RaContentRules.CheckPrefixLifetimes(TimeSpan.FromDays(-1), TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2001:db8::1", 64, "2001:db8::")]
    [InlineData("2001:db8:0:1:ffff::1", 68, "2001:db8:0:1:f000::")]
    [InlineData("2001:db8::1", 128, "2001:db8::1")]
    [InlineData("2001:db8::1", 0, "::")]
    public void TryNormalizePrefix_WithHostBitsSet_ClearsThem(
        string text,
        int length,
        string expected
    )
    {
        Assert.True(RaContentRules.TryNormalizePrefix(text, length, out IPAddress masked));
        Assert.Equal(IPAddress.Parse(expected), masked);
    }

    [Fact]
    public void TryNormalizePrefix_WithAnIpv4OrLinkLocalPrefix_Fails()
    {
        Assert.False(RaContentRules.TryNormalizePrefix("192.0.2.0", 24, out _));
        Assert.False(RaContentRules.TryNormalizePrefix("fe80::", 64, out _));
    }

    [Fact]
    public void TryNormalizeRoutePrefix_WithLengthZero_YieldsTheUnspecifiedAddress()
    {
        Assert.True(RaContentRules.TryNormalizeRoutePrefix(null, 0, out IPAddress masked));
        Assert.Equal(IPAddress.IPv6Any, masked);
    }

    [Theory]
    [InlineData(3, 4, true)]
    [InlineData(2, 4, false)]
    [InlineData(3, 3, false)]
    [InlineData(198, 600, true)]
    [InlineData(450, 600, true)]
    [InlineData(451, 600, false)]
    [InlineData(3, 65535, true)]
    [InlineData(3, 65536, false)]
    public void CheckIntervals_AtAndAroundEachBound_AcceptsOnlyTheRange(
        int minSeconds,
        int maxSeconds,
        bool valid
    )
    {
        string error = RaContentRules.CheckIntervals(
            TimeSpan.FromSeconds(minSeconds),
            TimeSpan.FromSeconds(maxSeconds)
        );

        Assert.Equal(valid, error is null);
    }
}
