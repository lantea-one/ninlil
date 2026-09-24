using System.Net;
using Ninlil.Dns.Server.Authoritative.Ptr;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Ptr;

/// <summary>
///     This class covers <see cref="ReverseZoneNameCalculator" /> against known-good vectors --
///     the IPv4 case against RFC 1035 §3.5's own conventional octet-boundary example, and the
///     IPv6 case against the well-known <c>2001:db8::/32</c> documentation prefix (RFC 3849),
///     whose own <c>/64</c> reverse zone name (<c>0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa</c>) is
///     independently verifiable against any real-world <c>ip6.arpa</c> reference.
/// </summary>
public sealed class ReverseZoneNameCalculatorTests
{
    [Fact]
    public void ComputeForAnIpv4AddressReturnsTheEnclosingSlash24ZoneAndOwnerName()
    {
        (string zoneName, string ownerName) = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("192.0.2.5")
        );

        Assert.Equal("2.0.192.in-addr.arpa", zoneName);
        Assert.Equal("5.2.0.192.in-addr.arpa", ownerName);
    }

    [Fact]
    public void ComputeForAnIpv6AddressReturnsTheEnclosingSlash64ZoneAndOwnerName()
    {
        (string zoneName, string ownerName) = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("2001:db8::1")
        );

        Assert.Equal("0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa", zoneName);
        Assert.Equal(
            "1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa",
            ownerName
        );
    }

    [Fact]
    public void ComputeForTwoAddressesInTheSameSlash64ReturnsTheSameZoneName()
    {
        (string zoneNameA, _) = ReverseZoneNameCalculator.Compute(IPAddress.Parse("2001:db8::1"));
        (string zoneNameB, _) = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("2001:db8::abcd")
        );

        Assert.Equal(zoneNameA, zoneNameB);
    }

    [Fact]
    public void ComputeForTwoAddressesInDifferentSlash24sReturnsDifferentZoneNames()
    {
        (string zoneNameA, _) = ReverseZoneNameCalculator.Compute(IPAddress.Parse("192.0.2.5"));
        (string zoneNameB, _) = ReverseZoneNameCalculator.Compute(IPAddress.Parse("192.0.3.5"));

        Assert.NotEqual(zoneNameA, zoneNameB);
    }

    [Fact]
    public void ComputeAtASlash24PrefixHasNoDelegationGlue()
    {
        ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("192.0.2.5"),
            ipv4PrefixBits: 24
        );

        Assert.Null(computation.DelegationParentZoneName);
        Assert.Null(computation.DelegationParentOwnerName);
    }

    /// <summary>
    ///     This test checks the RFC 2317 §4 worked example directly: a <c>/25</c> block starting
    ///     at <c>.0</c> within <c>192.0.2.0/24</c> is named <c>0/25.2.0.192.in-addr.arpa</c>, and
    ///     an address inside it delegates from the octet-aligned parent zone.
    /// </summary>
    [Fact]
    public void ComputeAtAClasslessSlash25PrefixReturnsTheRfc2317ZoneNameAndDelegationGlue()
    {
        ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("192.0.2.5"),
            ipv4PrefixBits: 25
        );

        Assert.Equal("0/25.2.0.192.in-addr.arpa", computation.ZoneName);
        Assert.Equal("5.0/25.2.0.192.in-addr.arpa", computation.OwnerName);
        Assert.Equal("2.0.192.in-addr.arpa", computation.DelegationParentZoneName);
        Assert.Equal("5.2.0.192.in-addr.arpa", computation.DelegationParentOwnerName);
    }

    /// <summary>
    ///     This test checks the upper half of the same <c>/24</c>, which starts at <c>.128</c>,
    ///     not <c>.0</c> -- confirming the block's own start octet is actually computed from the
    ///     address, not hardcoded to zero.
    /// </summary>
    [Fact]
    public void ComputeAtAClasslessSlash25PrefixForTheUpperHalfUsesTheRealBlockStart()
    {
        ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("192.0.2.200"),
            ipv4PrefixBits: 25
        );

        Assert.Equal("128/25.2.0.192.in-addr.arpa", computation.ZoneName);
        Assert.Equal("200.128/25.2.0.192.in-addr.arpa", computation.OwnerName);
        Assert.Equal("2.0.192.in-addr.arpa", computation.DelegationParentZoneName);
        Assert.Equal("200.2.0.192.in-addr.arpa", computation.DelegationParentOwnerName);
    }

    /// <summary>
    ///     This test checks a finer classless grouping (<c>/28</c>, 16 addresses per block) against
    ///     a block that does not start at a "nice" boundary like 0 or 128, to prove the modulo-based
    ///     block-start computation generalizes correctly to any supported prefix length.
    /// </summary>
    [Fact]
    public void ComputeAtAClasslessSlash28PrefixComputesTheRightBlockStart()
    {
        ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("192.0.2.37"),
            ipv4PrefixBits: 28
        );

        // 37 falls in the block [32, 48) -- 16 addresses per /28 block.
        Assert.Equal("32/28.2.0.192.in-addr.arpa", computation.ZoneName);
        Assert.Equal("37.32/28.2.0.192.in-addr.arpa", computation.OwnerName);
        Assert.Equal("2.0.192.in-addr.arpa", computation.DelegationParentZoneName);
        Assert.Equal("37.2.0.192.in-addr.arpa", computation.DelegationParentOwnerName);
    }

    [Fact]
    public void ComputeForAnIpv6AddressAtACustomPrefixNeverReturnsDelegationGlue()
    {
        ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("2001:db8::1"),
            ipv6PrefixBits: 48
        );

        Assert.Null(computation.DelegationParentZoneName);
        Assert.Null(computation.DelegationParentOwnerName);
    }

    [Fact]
    public void ComputeForAnIpv6AddressAtACustomPrefixUsesThatPrefix()
    {
        (string zoneName, _) = ReverseZoneNameCalculator.Compute(
            IPAddress.Parse("2001:db8::1"),
            ipv6PrefixBits: 48
        );

        // A /48 zone has 12 nibbles, four fewer labels than the default /64's own 16.
        Assert.Equal("0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa", zoneName);
    }

    [Theory]
    [InlineData("5.0/25.2.0.192.in-addr.arpa", "2.0.192.in-addr.arpa", "5.2.0.192.in-addr.arpa")]
    [InlineData("37.32/28.2.0.192.in-addr.arpa", "2.0.192.in-addr.arpa", "37.2.0.192.in-addr.arpa")]
    public void TryGetClasslessDelegationParentParsesAClasslessOwnerNameBackIntoItsParent(
        string classlessOwnerName,
        string expectedParentZoneName,
        string expectedParentOwnerName
    )
    {
        bool parsed = ReverseZoneNameCalculator.TryGetClasslessDelegationParent(
            classlessOwnerName,
            out string parentZoneName,
            out string parentOwnerName
        );

        Assert.True(parsed);
        Assert.Equal(expectedParentZoneName, parentZoneName);
        Assert.Equal(expectedParentOwnerName, parentOwnerName);
    }

    [Theory]
    [InlineData("5.2.0.192.in-addr.arpa")]
    [InlineData("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa")]
    public void TryGetClasslessDelegationParentReturnsFalseForAnOrdinaryOwnerName(string ownerName)
    {
        bool parsed = ReverseZoneNameCalculator.TryGetClasslessDelegationParent(
            ownerName,
            out string parentZoneName,
            out string parentOwnerName
        );

        Assert.False(parsed);
        Assert.Null(parentZoneName);
        Assert.Null(parentOwnerName);
    }
}
