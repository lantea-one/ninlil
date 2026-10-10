using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Filtering.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers how <see cref="DnsFilterEngine" /> reads from
///     <see cref="DnsFilterConfigurationCache" /> and <see cref="DnsClientDirectory" />: a row
///     change takes effect on the next decision once the change signal fires for its entity type
///     (and not before, which proves the decision came from the cache), no database scope is
///     opened while the copies are fresh, and decisions continue from the last copy when the
///     database stops answering. The tests change the in-memory repositories directly and raise
///     the signal through the fixture's real <see cref="EntityChangeNotifier" />, the way a
///     committed save does.
/// </summary>
public class DnsFilterEngineCacheTests
{
    private static readonly DnsName Blocked = DnsName.Parse("ads.example.com");
    private static readonly IPAddress Anyone = IPAddress.Parse("10.9.9.9");

    /// <summary>
    ///     This method asserts the cache contract for one change: the decision is
    ///     <paramref name="before" /> and stays so after <paramref name="mutate" /> until the
    ///     signal for <paramref name="changed" /> fires, then flips.
    /// </summary>
    private static async Task AssertChangeAppliesOnSignalAsync(
        DnsFilterEngineTests.Fixture fixture,
        IPAddress address,
        bool before,
        Action mutate,
        Type changed
    )
    {
        Assert.Equal(before, await IsDeniedAsync(fixture.Engine, address));

        mutate();

        Assert.Equal(before, await IsDeniedAsync(fixture.Engine, address));

        fixture.Notifier.NotifyChanged([changed]);

        Assert.Equal(!before, await IsDeniedAsync(fixture.Engine, address));
    }

    private static async Task<bool> IsDeniedAsync(DnsFilterEngine engine, IPAddress address) =>
        (await engine.EvaluateDomainAsync(Blocked, address)).IsDenied;

    /// <summary>
    ///     This method builds a default group with a block list for <see cref="Blocked" />.
    /// </summary>
    private static (DnsClientGroupEntity Group, DnsFilterListEntity List) DefaultGroupWithBlockList(
        DnsFilterEngineTests.Fixture fixture
    )
    {
        DnsClientGroupEntity group = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        fixture.AssignListToGroup(group.Id, list.Id);

        return (group, list);
    }

    [Fact]
    public async Task EvaluateDomainAsync_AGroupThatIsDisabled_StopsApplyingOnTheGroupSignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        (DnsClientGroupEntity group, _) = DefaultGroupWithBlockList(fixture);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            Anyone,
            before: true,
            () => group.IsDisabled = true,
            typeof(DnsClientGroupEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_ANetworkAddedToAGroup_AppliesOnTheNetworkSignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity kids = fixture.AddGroup("kids", isDefault: false);
        DnsFilterListEntity list = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        fixture.AssignListToGroup(kids.Id, list.Id);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            Anyone,
            before: false,
            () =>
                fixture.Networks.Items.Add(
                    new DnsClientGroupNetworkEntity
                    {
                        Id = Guid.NewGuid(),
                        ClientGroupId = kids.Id,
                        Cidr = "10.9.0.0/16",
                    }
                ),
            typeof(DnsClientGroupNetworkEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_AGroupListAssignmentThatIsRemoved_StopsApplyingOnTheAssignmentSignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DefaultGroupWithBlockList(fixture);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            Anyone,
            before: true,
            fixture.Assignments.Items.Clear,
            typeof(DnsClientGroupListAssignmentEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_AListThatIsDisabled_StopsApplyingOnTheListSignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        (_, DnsFilterListEntity list) = DefaultGroupWithBlockList(fixture);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            Anyone,
            before: true,
            () => list.IsDisabled = true,
            typeof(DnsFilterListEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_AnEntryThatIsRemoved_StopsApplyingOnTheEntrySignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DefaultGroupWithBlockList(fixture);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            Anyone,
            before: true,
            fixture.Entries.Items.Clear,
            typeof(DnsFilterListEntryEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_AnEntryAddedToAList_AppliesOnTheEntrySignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        (_, DnsFilterListEntity list) = DefaultGroupWithBlockList(fixture);
        DnsName other = DnsName.Parse("tracker.example.net");

        Assert.False((await fixture.Engine.EvaluateDomainAsync(other, Anyone)).IsDenied);

        fixture.Entries.Items.Add(
            new DnsFilterListEntryEntity
            {
                Id = Guid.NewGuid(),
                ListId = list.Id,
                Value = "tracker.example.net",
            }
        );
        fixture.Notifier.NotifyChanged([typeof(DnsFilterListEntryEntity)]);

        Assert.True((await fixture.Engine.EvaluateDomainAsync(other, Anyone)).IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_AClientThatIsDisabled_LosesItsListsOnTheClientSignal()
    {
        IPAddress address = IPAddress.Parse("10.0.0.43");
        FakeClientIdentityResolver resolver = new(
            address,
            new ClientIdentityResolution("11:22:33:44:55:66", InternetProtocolVersionEnum.IPv4)
        );
        DnsFilterEngineTests.Fixture fixture = new(resolver);
        fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        DnsClientEntity client = fixture.AddClient("11:22:33:44:55:66");
        fixture.AssignListToClient(client.Id, list.Id);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            address,
            before: true,
            () => client.IsDisabled = true,
            typeof(DnsClientEntity)
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_AClientAddressThatIsRemoved_LosesItsListsOnTheAddressSignal()
    {
        IPAddress address = IPAddress.Parse("10.0.0.43");
        FakeClientIdentityResolver resolver = new(
            address,
            new ClientIdentityResolution("11:22:33:44:55:66", InternetProtocolVersionEnum.IPv4)
        );
        DnsFilterEngineTests.Fixture fixture = new(resolver);
        fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        DnsClientEntity client = fixture.AddClient("11:22:33:44:55:66");
        fixture.AssignListToClient(client.Id, list.Id);

        await AssertChangeAppliesOnSignalAsync(
            fixture,
            address,
            before: true,
            fixture.ClientAddresses.Items.Clear,
            typeof(DnsClientAddressEntity)
        );
    }

    [Fact]
    public async Task EvaluateClientAsync_ASourceListThatIsEdited_AppliesOnTheEntrySignal()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DnsClientGroupEntity group = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddList(
            "banned",
            DnsFilterListKindEnum.ClientSource,
            DnsFilterListActionEnum.Block,
            "10.9.0.0/16"
        );
        fixture.AssignListToGroup(group.Id, list.Id);

        Assert.True((await fixture.Engine.EvaluateClientAsync(Anyone)).IsDenied);

        fixture.Entries.Items.Clear();
        fixture.Notifier.NotifyChanged([typeof(DnsFilterListEntryEntity)]);

        Assert.False((await fixture.Engine.EvaluateClientAsync(Anyone)).IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_WithFreshCopies_OpensNoScope()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DefaultGroupWithBlockList(fixture);
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsFilterEngine engine = new(
            new DnsFilterConfigurationCache(
                scopes,
                fixture.Notifier,
                NullLogger<DnsFilterConfigurationCache>.Instance
            ),
            new StaticOptionsMonitor<DnsFilterOptions>(fixture.Options),
            NullLogger<DnsFilterEngine>.Instance
        );

        Assert.True(await IsDeniedAsync(engine, Anyone));
        int afterFirst = scopes.Count;

        for (int index = 0; index < 20; index++)
            Assert.True(await IsDeniedAsync(engine, Anyone));

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, scopes.Count);
    }

    [Fact]
    public async Task EvaluateDomainAsync_WhenTheDatabaseStopsAnswering_KeepsDecidingFromTheLastCopy()
    {
        DnsFilterEngineTests.Fixture fixture = new();
        DefaultGroupWithBlockList(fixture);
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsFilterEngine engine = new(
            new DnsFilterConfigurationCache(
                scopes,
                fixture.Notifier,
                NullLogger<DnsFilterConfigurationCache>.Instance
            ),
            new StaticOptionsMonitor<DnsFilterOptions>(fixture.Options),
            NullLogger<DnsFilterEngine>.Instance
        );

        Assert.True(await IsDeniedAsync(engine, Anyone));

        scopes.Fail = true;

        // A signal makes the next decision try a reload; the failure keeps the last copy.
        fixture.Notifier.NotifyChanged([
            typeof(DnsFilterListEntryEntity),
            typeof(DnsFilterListEntity),
        ]);

        Assert.True(await IsDeniedAsync(engine, Anyone));
        Assert.True(await IsDeniedAsync(engine, Anyone));
    }

    /// <summary>
    ///     This class wraps an <see cref="IServiceScopeFactory" />, counting the scopes opened
    ///     and optionally failing like a database that is down.
    /// </summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _count;

        public bool Fail;

        public int Count => Volatile.Read(ref _count);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _count);

            return Fail
                ? throw new InvalidOperationException("The database is down.")
                : inner.CreateScope();
        }
    }
}
