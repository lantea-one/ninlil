using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests.ProxyProtocol;

/// <summary>
///     This class tests <see cref="ProxyProtocolTrustEvaluator" />: parsing the allowlist and
///     matching a peer address against it, including the IPv4-mapped IPv6 addresses a dual-stack
///     socket reports.
/// </summary>
public sealed class ProxyProtocolTrustEvaluatorTests
{
    [Fact]
    public void ParseKnownSources_SkipsAMalformedAddressButKeepsTheValidOnes()
    {
        ProxyProtocolOptions options = new()
        {
            KnownProxies = ["203.0.113.10", "not-an-ip-address"],
        };

        ProxyProtocolTrustEvaluator.ParseKnownSources(
            options,
            NullLogger.Instance,
            out List<IPAddress> knownAddresses,
            out List<IPNetwork> knownNetworks
        );

        Assert.Single(knownAddresses);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), knownAddresses[0]);
        Assert.Empty(knownNetworks);
    }

    [Fact]
    public void ParseKnownSources_SkipsAMalformedNetworkButKeepsTheValidOnes()
    {
        ProxyProtocolOptions options = new()
        {
            KnownNetworks = ["10.0.0.0/24", "not-a-cidr-network"],
        };

        ProxyProtocolTrustEvaluator.ParseKnownSources(
            options,
            NullLogger.Instance,
            out List<IPAddress> knownAddresses,
            out List<IPNetwork> knownNetworks
        );

        Assert.Empty(knownAddresses);
        Assert.Single(knownNetworks);
        Assert.Equal(IPNetwork.Parse("10.0.0.0/24"), knownNetworks[0]);
    }

    [Fact]
    public void IsKnownSource_AnExactlyListedAddress_IsTrusted()
    {
        bool result = ProxyProtocolTrustEvaluator.IsKnownSource(
            IPAddress.Parse("203.0.113.10"),
            [IPAddress.Parse("203.0.113.10")],
            []
        );

        Assert.True(result);
    }

    [Fact]
    public void IsKnownSource_AnAddressInsideAKnownNetwork_IsTrusted()
    {
        bool result = ProxyProtocolTrustEvaluator.IsKnownSource(
            IPAddress.Parse("10.0.0.42"),
            [],
            [IPNetwork.Parse("10.0.0.0/24")]
        );

        Assert.True(result);
    }

    [Fact]
    public void IsKnownSource_AnAddressMatchingNeitherList_IsNotTrusted()
    {
        bool result = ProxyProtocolTrustEvaluator.IsKnownSource(
            IPAddress.Parse("198.51.100.1"),
            [IPAddress.Parse("203.0.113.10")],
            [IPNetwork.Parse("10.0.0.0/24")]
        );

        Assert.False(result);
    }

    [Fact]
    public void IsKnownSource_AnIpv4MappedToIpv6Address_MatchesItsPlainIpv4Entry()
    {
        // On a dual-stack socket an IPv4 peer arrives as ::ffff:a.b.c.d; an entry in plain IPv4
        // form must still match.
        bool result = ProxyProtocolTrustEvaluator.IsKnownSource(
            IPAddress.Parse("::ffff:203.0.113.10"),
            [IPAddress.Parse("203.0.113.10")],
            []
        );

        Assert.True(result);
    }

    [Fact]
    public void IsKnownSource_AnIpv4MappedToIpv6Address_MatchesItsPlainIpv4Network()
    {
        bool result = ProxyProtocolTrustEvaluator.IsKnownSource(
            IPAddress.Parse("::ffff:10.0.0.42"),
            [],
            [IPNetwork.Parse("10.0.0.0/24")]
        );

        Assert.True(result);
    }
}
