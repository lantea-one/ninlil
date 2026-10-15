using System.Net;
using Ninlil.Dhcp.Server.Core.Network;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the prefix arithmetic of <see cref="Dhcpv6NetworkHelper" /> that the
///     delegated-prefix allocation relies on: alignment and overlap. It is pure and uses no host.
/// </summary>
public sealed class Dhcpv6NetworkHelperTests
{
    [Theory]
    [InlineData("2001:db8:0:1::", 56, false)]
    [InlineData("2001:db8:0:100::", 56, true)]
    [InlineData("2001:db8::", 48, true)]
    [InlineData("2001:db8:0:10::", 60, true)]
    [InlineData("2001:db8:0:10::", 52, false)]
    public void IsAlignedPrefix_ChecksThatNoHostBitsAreSet(
        string prefix,
        int prefixLength,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            Dhcpv6NetworkHelper.IsAlignedPrefix(IPAddress.Parse(prefix), prefixLength)
        );

    [Theory]
    [InlineData("2001:db8::", 52, "2001:db8:0:100::", 56, true)] // nested
    [InlineData("2001:db8:0:100::", 56, "2001:db8::", 52, true)] // nested, other order
    [InlineData("2001:db8::", 56, "2001:db8::", 56, true)] // equal
    [InlineData("2001:db8::", 56, "2001:db8:0:100::", 56, false)] // disjoint, same length
    [InlineData("2001:db8::", 52, "2001:db8:0:1000::", 56, false)] // disjoint, different length
    public void PrefixesOverlap_TreatsEqualAndNestedPrefixesAsOverlapping(
        string first,
        int firstLength,
        string second,
        int secondLength,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            Dhcpv6NetworkHelper.PrefixesOverlap(
                IPAddress.Parse(first),
                firstLength,
                IPAddress.Parse(second),
                secondLength
            )
        );
}
