using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests.ProxyProtocol;

/// <summary>
///     This class covers <see cref="ProxyProtocolTrustEvaluator" /> directly -- parsing a
///     configured allowlist and matching a connecting peer's own address against it, including the
///     IPv4-mapped-to-IPv6 handling this listener's own dual-stack socket needs.
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
        // This listener binds a dual-stack IPv6 socket, so an IPv4 peer's own address arrives
        // mapped into IPv6 form (::ffff:a.b.c.d) -- an operator configuring the plain IPv4 form
        // must still match.
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
