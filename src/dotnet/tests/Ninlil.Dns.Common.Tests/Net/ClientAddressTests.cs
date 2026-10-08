using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Net;

/// <summary>
///     This class tests <see cref="ClientAddress" /> with plain in-memory addresses: the
///     canonical form of IPv4-mapped and zone-indexed addresses, and matching against bare
///     addresses and CIDR networks.
/// </summary>
public sealed class ClientAddressTests
{
    [Fact]
    public void Normalize_TurnsAnIPv4MappedAddressIntoPlainIPv4()
    {
        IPAddress result = ClientAddress.Normalize(IPAddress.Parse("::ffff:192.0.2.9"));

        Assert.Equal(AddressFamily.InterNetwork, result.AddressFamily);
        Assert.Equal(IPAddress.Parse("192.0.2.9"), result);
    }

    [Fact]
    public void Normalize_DropsTheScopeIdOfAnIPv6Address()
    {
        IPAddress result = ClientAddress.Normalize(IPAddress.Parse("fe80::1%3"));

        Assert.Equal(0, result.ScopeId);
        Assert.Equal(IPAddress.Parse("fe80::1"), result);
    }

    [Theory]
    [InlineData("192.0.2.9")]
    [InlineData("2001:db8::1")]
    [InlineData("::1")]
    public void Normalize_ReturnsAPlainAddressAsTheSameInstance(string text)
    {
        IPAddress address = IPAddress.Parse(text);

        Assert.Same(address, ClientAddress.Normalize(address));
    }

    [Fact]
    public void Normalize_ReturnsNullForNull() => Assert.Null(ClientAddress.Normalize(null));

    [Fact]
    public void IsSameAddress_MatchesMappedAndZoneIndexedFormsAgainstTheConfiguredAddress()
    {
        Assert.True(
            ClientAddress.IsSameAddress(
                IPAddress.Parse("192.0.2.1"),
                IPAddress.Parse("::ffff:192.0.2.1")
            )
        );
        Assert.True(
            ClientAddress.IsSameAddress(IPAddress.Parse("fe80::1"), IPAddress.Parse("fe80::1%2"))
        );
        Assert.False(
            ClientAddress.IsSameAddress(
                IPAddress.Parse("192.0.2.1"),
                IPAddress.Parse("::ffff:192.0.2.2")
            )
        );
        Assert.False(ClientAddress.IsSameAddress(null, IPAddress.Parse("192.0.2.1")));
        Assert.False(ClientAddress.IsSameAddress(null, null));
    }

    [Theory]
    [InlineData("192.0.2.0/24", "::ffff:192.0.2.9", true)]
    [InlineData("192.0.2.0/24", "192.0.2.9", true)]
    [InlineData("192.0.2.0/24", "::ffff:192.0.3.9", false)]
    [InlineData("::ffff:192.0.2.0/120", "192.0.2.9", true)]
    [InlineData("::ffff:192.0.2.0/120", "192.0.3.9", false)]
    [InlineData("192.0.2.9", "::ffff:192.0.2.9", true)]
    [InlineData("::ffff:192.0.2.9", "192.0.2.9", true)]
    [InlineData("fe80::1", "fe80::1%4", true)]
    [InlineData("fe80::/10", "fe80::1%4", true)]
    [InlineData("2001:db8::/32", "2001:db8::5", true)]
    [InlineData("2001:db8::/32", "192.0.2.9", false)]
    public void NetworkContains_MatchesBareAddressesAndCidrNetworksAfterNormalizing(
        string entry,
        string address,
        bool expected
    ) => Assert.Equal(expected, ClientAddress.NetworkContains(entry, IPAddress.Parse(address)));

    [Theory]
    [InlineData("not an address")]
    [InlineData("192.0.2.0/99")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NetworkContains_TreatsAnEntryThatDoesNotParseAsMatchingNothing(string entry) =>
        Assert.False(ClientAddress.NetworkContains(entry, IPAddress.Parse("192.0.2.5")));

    [Fact]
    public void NetworkContains_MatchesNothingForANullAddress() =>
        Assert.False(ClientAddress.NetworkContains("192.0.2.0/24", null));
}
