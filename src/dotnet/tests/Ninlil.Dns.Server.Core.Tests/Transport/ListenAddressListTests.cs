using System.Net;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class covers how a transport's <c>ListenAddresses</c> setting is split, parsed,
///     validated for the settings API, and resolved leniently for a listener about to bind.
/// </summary>
public class ListenAddressListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ; ")]
    public void Split_OfNothing_IsEmpty(string configured) =>
        Assert.Empty(ListenAddressList.Split(configured));

    [Fact]
    public void Split_AcceptsCommasSemicolonsAndWhitespace()
    {
        IReadOnlyList<string> entries = ListenAddressList.Split(
            "127.0.0.1, ::1;192.168.1.1\n fd00::1  0.0.0.0"
        );

        Assert.Equal(["127.0.0.1", "::1", "192.168.1.1", "fd00::1", "0.0.0.0"], entries);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("0.0.0.0", "0.0.0.0")]
    [InlineData("192.168.100.254", "192.168.100.254")]
    [InlineData("::", "::")]
    [InlineData("::1", "::1")]
    [InlineData("fd00::53", "fd00::53")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    [InlineData("::ffff:10.0.0.1", "10.0.0.1")]
    public void TryParseAddress_AcceptsIPLiterals(string entry, string expected)
    {
        Assert.True(ListenAddressList.TryParseAddress(entry, out IPAddress address));
        Assert.Equal(IPAddress.Parse(expected), address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eth0")]
    [InlineData("localhost")]
    [InlineData("dns.example.com")]
    [InlineData("127.1")]
    [InlineData("1")]
    [InlineData("010.0.0.1")]
    [InlineData("256.0.0.1")]
    [InlineData("127.0.0.1:53")]
    [InlineData("[::1]")]
    [InlineData("[::1]:53")]
    [InlineData("::1::2")]
    [InlineData("192.168.1.0/24")]
    public void TryParseAddress_RefusesAnythingButAPlainLiteral(string entry) =>
        Assert.False(ListenAddressList.TryParseAddress(entry, out _));

    [Fact]
    public void TryValidate_NormalizesAndDropsDuplicates()
    {
        Assert.True(
            ListenAddressList.TryValidate(
                ["127.0.0.1", " ::1 ", "127.0.0.1", "::FFFF:127.0.0.1", "0:0:0:0:0:0:0:1"],
                out List<IPAddress> addresses,
                out string error
            )
        );

        Assert.Null(error);
        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], addresses);
    }

    [Fact]
    public void TryValidate_EmptyList_IsValidAndMeansEveryAddress()
    {
        Assert.True(ListenAddressList.TryValidate([], out List<IPAddress> addresses, out _));
        Assert.Empty(addresses);
    }

    [Fact]
    public void TryValidate_BothWildcards_AreAllowedTogether()
    {
        Assert.True(
            ListenAddressList.TryValidate(["0.0.0.0", "::"], out List<IPAddress> addresses, out _)
        );
        Assert.Equal([IPAddress.Any, IPAddress.IPv6Any], addresses);
    }

    [Fact]
    public void TryValidate_MalformedEntry_NamesTheEntry()
    {
        Assert.False(
            ListenAddressList.TryValidate(
                ["127.0.0.1", "eth0"],
                out List<IPAddress> addresses,
                out string error
            )
        );

        Assert.Empty(addresses);
        Assert.Contains("'eth0'", error);
        Assert.Contains("not a valid IP address", error);
    }

    [Theory]
    [InlineData("0.0.0.0", "192.168.1.1")]
    [InlineData("::", "fd00::1")]
    public void TryValidate_AddressAlreadyCoveredByItsWildcard_IsRefused(
        string wildcard,
        string specific
    )
    {
        Assert.False(ListenAddressList.TryValidate([specific, wildcard], out _, out string error));

        Assert.Contains($"'{specific}'", error);
        Assert.Contains($"'{wildcard}'", error);
    }

    [Fact]
    public void TryValidate_WildcardOfTheOtherFamily_DoesNotCoverAnAddress() =>
        Assert.True(ListenAddressList.TryValidate(["0.0.0.0", "::1"], out _, out _));

    [Fact]
    public void Resolve_NothingConfigured_ListensEverywhere()
    {
        ListenAddressResolution resolution = ListenAddressList.Resolve("");

        Assert.True(resolution.ListensEverywhere);
        Assert.Empty(resolution.Addresses);
        Assert.Empty(resolution.InvalidEntries);
    }

    [Fact]
    public void Resolve_SkipsInvalidEntriesAndKeepsTheRest()
    {
        ListenAddressResolution resolution = ListenAddressList.Resolve(
            "127.0.0.1, eth0, ::1, 127.0.0.1"
        );

        Assert.False(resolution.ListensEverywhere);
        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], resolution.Addresses);
        Assert.Equal(["eth0"], resolution.InvalidEntries);
    }

    [Fact]
    public void Resolve_OnlyInvalidEntries_ListensNowhereRatherThanEverywhere()
    {
        // Falling back to "every address" here would expose the transport on exactly the
        // interfaces the operator was trying to keep it off.
        ListenAddressResolution resolution = ListenAddressList.Resolve("eth0, wan0");

        Assert.False(resolution.ListensEverywhere);
        Assert.Empty(resolution.Addresses);
        Assert.Equal(["eth0", "wan0"], resolution.InvalidEntries);
    }

    [Fact]
    public void Resolve_DropsAnAddressItsWildcardAlreadyCovers()
    {
        ListenAddressResolution resolution = ListenAddressList.Resolve("127.0.0.1, 0.0.0.0, ::1");

        Assert.Equal([IPAddress.Any, IPAddress.IPv6Loopback], resolution.Addresses);
        Assert.Equal([IPAddress.Loopback], resolution.CoveredAddresses);
        Assert.Empty(resolution.InvalidEntries);
    }

    [Fact]
    public void Join_WritesTheCommaSeparatedFormResolveReadsBack()
    {
        string joined = ListenAddressList.Join([IPAddress.Loopback, IPAddress.IPv6Loopback]);

        Assert.Equal("127.0.0.1, ::1", joined);
        Assert.Equal(
            [IPAddress.Loopback, IPAddress.IPv6Loopback],
            ListenAddressList.Resolve(joined).Addresses
        );
    }
}
