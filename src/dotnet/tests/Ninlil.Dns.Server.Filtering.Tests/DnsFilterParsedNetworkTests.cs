using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers the networks the filter parses once when it loads them: group network
///     rows (<see cref="DnsFilterGroupNetwork" />) and network-list entries
///     (<see cref="DnsFilterListIndex.ContainsAddress" />). It checks that a row or entry that
///     does not parse matches nothing and leaves the others working, and that a query-time match
///     allocates nothing, which shows the text is not parsed again. The engine tests run over the
///     in-memory repositories of <see cref="DnsFilterEngineTests.Fixture" />.
/// </summary>
public sealed class DnsFilterParsedNetworkTests
{
    [Fact]
    public void GroupNetwork_ParsesItsCidrOnceAndComparesByText()
    {
        DnsFilterGroupNetwork first = new("10.0.0.0/8", null);
        DnsFilterGroupNetwork second = new("10.0.0.0/8", null);

        Assert.NotNull(first.Network);
        Assert.Same(first.Network, first.Network);
        Assert.True(first.Network.Contains(IPAddress.Parse("10.2.3.4")));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new DnsFilterGroupNetwork("10.0.0.0/9", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a network")]
    [InlineData("10.0.0.0/99")]
    public void GroupNetwork_WithoutAParsableCidr_HasNoNetwork(string cidr) =>
        Assert.Null(new DnsFilterGroupNetwork(cidr, "label").Network);

    [Fact]
    public async Task EvaluateDomainAsync_AGroupNetworkThatDoesNotParse_MatchesNothingAndTheOthersStillMatch()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity defaultBlockList = fixture.AddList(
            "default-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, defaultBlockList.Id);

        DnsClientGroupEntity brokenGroup = fixture.AddGroup("broken", isDefault: false);
        fixture.AddNetwork(brokenGroup.Id, "not a network");
        fixture.AddNetwork(brokenGroup.Id, "10.0.0.0/99");

        DnsClientGroupEntity kidsGroup = fixture.AddGroup("kids", isDefault: false);
        fixture.AddNetwork(kidsGroup.Id, "junk");
        fixture.AddNetwork(kidsGroup.Id, "192.168.1.0/24");

        // The kids row after the broken one still matches, so the kids group (no lists) wins and
        // the default group's block list does not apply.
        DnsFilterDecision inKids = await fixture.Engine.EvaluateDomainAsync(
            DnsName.Parse("blocked.example.com"),
            IPAddress.Parse("192.168.1.50")
        );
        Assert.False(inKids.IsDenied);

        // No row matches this client, so the default group applies.
        DnsFilterDecision elsewhere = await fixture.Engine.EvaluateDomainAsync(
            DnsName.Parse("blocked.example.com"),
            IPAddress.Parse("172.16.0.9")
        );
        Assert.True(elsewhere.IsDenied);
    }

    [Fact]
    public void ContainsAddress_AnEntryThatDoesNotParse_MatchesNothingAndTheOthersStillMatch()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.ClientSource,
            [Entry("junk"), Entry(null), Entry("10.0.0.0/99"), Entry("  "), Entry("192.0.2.0/24")]
        );

        Assert.Equal(5, index.EntryCount);
        Assert.True(index.ContainsAddress(IPAddress.Parse("192.0.2.77")));
        Assert.True(index.ContainsAddress(IPAddress.Parse("::ffff:192.0.2.77")));
        Assert.False(index.ContainsAddress(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void ContainsAddress_AfterTheFirstCall_AllocatesNothingPerQuery()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.ResponseIp,
            [
                Entry("10.0.0.0/8"),
                Entry("2001:db8::/32"),
                Entry("192.0.2.7"),
                Entry("198.51.100.0/24"),
            ]
        );
        IPAddress client = IPAddress.Parse("198.51.100.9");

        // Warm up so JIT and lazy statics do not count.
        for (int i = 0; i < 100; i++)
            Assert.True(index.ContainsAddress(client));

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
            index.ContainsAddress(client);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static DnsFilterListEntryEntity Entry(string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            ListId = Guid.NewGuid(),
            Value = value,
        };
}
