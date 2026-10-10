using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.ClientSchedule.Tests.Fakes;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class covers how <see cref="DnsClientScheduleEngine" /> reads from
///     <see cref="DnsClientScheduleCache" /> and <see cref="DnsClientDirectory" />: a saved
///     schedule, pause or group change applies to the very next evaluation (read-your-own-writes),
///     fresh copies cost no database scope, and a database that stops answering leaves the last
///     copy in use. The tests reuse the in-memory fixture of
///     <see cref="DnsClientScheduleEngineTests" /> and raise the change signal the way a committed
///     save does.
/// </summary>
public sealed class DnsClientScheduleEngineCacheTests
{
    private static readonly IPAddress ClientAddress = IPAddress.Parse("203.0.113.10");
    private static readonly ClientIdentityResolution Resolution = new(
        "aa:bb:cc:dd:ee:ff",
        InternetProtocolVersionEnum.IPv4
    );

    // Monday 22:00 in "Asia/Tokyo" (UTC+9).
    private static readonly DateTimeOffset MondayNightTokyo = DateTimeOffset.Parse(
        "2026-09-14T13:00:00Z"
    );

    private static DnsScheduleDayOfWeekEnum EveryDay() =>
        DnsScheduleDayOfWeekEnum.Sunday
        | DnsScheduleDayOfWeekEnum.Monday
        | DnsScheduleDayOfWeekEnum.Tuesday
        | DnsScheduleDayOfWeekEnum.Wednesday
        | DnsScheduleDayOfWeekEnum.Thursday
        | DnsScheduleDayOfWeekEnum.Friday
        | DnsScheduleDayOfWeekEnum.Saturday;

    private static async Task<bool> IsBlockedAsync(DnsClientScheduleEngine engine) =>
        (await engine.EvaluateAsync(ClientAddress, MondayNightTokyo)).IsBlocked;

    [Fact]
    public async Task EvaluateAsync_AScheduleAddedAfterTheFirstEvaluation_BlocksTheNextOne()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        DnsClientEntity client = fixture.AddClient();
        Assert.False(await IsBlockedAsync(fixture.Engine));

        fixture.AddClientSchedule(client.Id, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));

        Assert.True(await IsBlockedAsync(fixture.Engine));
    }

    [Fact]
    public async Task EvaluateAsync_AScheduleDisabledOrRemoved_UnblocksTheNextEvaluation()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        Guid groupId = Guid.NewGuid();
        DnsClientEntity client = fixture.AddClient(groupId);
        fixture.AddClientSchedule(client.Id, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));
        fixture.AddGroupSchedule(groupId, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));
        Assert.True(await IsBlockedAsync(fixture.Engine));

        fixture.ClientSchedules.Items[0].IsDisabled = true;
        fixture.Changed();

        // The group schedule still blocks.
        Assert.True(await IsBlockedAsync(fixture.Engine));

        fixture.GroupSchedules.Items.Clear();
        fixture.Changed();

        Assert.False(await IsBlockedAsync(fixture.Engine));
    }

    [Fact]
    public async Task EvaluateAsync_AGroupPausedAfterTheFirstEvaluation_BlocksItsClients()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        Guid groupId = Guid.NewGuid();
        fixture.AddClient(groupId);
        DnsClientGroupEntity group = fixture.AddGroup(groupId);
        Assert.False(await IsBlockedAsync(fixture.Engine));

        group.PausedUntilUtc = MondayNightTokyo.AddHours(1);
        fixture.Changed();

        Assert.True(await IsBlockedAsync(fixture.Engine));

        group.PausedUntilUtc = null;
        fixture.Changed();

        Assert.False(await IsBlockedAsync(fixture.Engine));
    }

    [Fact]
    public async Task EvaluateAsync_AClientMovedToAnotherGroup_FollowsTheNewGroupsSchedule()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        Guid blockedGroup = Guid.NewGuid();
        DnsClientEntity client = fixture.AddClient();
        fixture.AddGroupSchedule(blockedGroup, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));
        Assert.False(await IsBlockedAsync(fixture.Engine));

        client.ClientGroupId = blockedGroup;
        fixture.Changed();

        Assert.True(await IsBlockedAsync(fixture.Engine));
    }

    [Fact]
    public async Task EvaluateAsync_AScheduleWithAnUnknownTimeZone_NeverBlocks()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            EveryDay(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            timeZoneId: "Nowhere/Atlantis"
        );

        Assert.False(await IsBlockedAsync(fixture.Engine));
    }

    [Fact]
    public async Task EvaluateAsync_WithFreshCopies_OpensNoScope()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        Guid groupId = Guid.NewGuid();
        DnsClientEntity client = fixture.AddClient(groupId);
        fixture.AddGroup(groupId);
        fixture.AddClientSchedule(client.Id, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsClientScheduleEngine engine = new(
            new DnsClientScheduleCache(
                scopes,
                fixture.Notifier,
                NullLogger<DnsClientScheduleCache>.Instance
            ),
            new FakeClientIdentityResolver(ClientAddress, Resolution),
            new DnsClientDirectory(
                scopes,
                fixture.Notifier,
                NullLogger<DnsClientDirectory>.Instance
            )
        );

        Assert.True(await IsBlockedAsync(engine));
        int afterFirst = scopes.Count;

        for (int index = 0; index < 20; index++)
            Assert.True(await IsBlockedAsync(engine));

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, scopes.Count);
    }

    [Fact]
    public async Task EvaluateAsync_WhenTheDatabaseStopsAnswering_KeepsDecidingFromTheLastCopy()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution)
        );
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(client.Id, EveryDay(), new TimeOnly(21, 0), new TimeOnly(7, 0));
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsClientScheduleEngine engine = new(
            new DnsClientScheduleCache(
                scopes,
                fixture.Notifier,
                NullLogger<DnsClientScheduleCache>.Instance
            ),
            new FakeClientIdentityResolver(ClientAddress, Resolution),
            new DnsClientDirectory(
                scopes,
                fixture.Notifier,
                NullLogger<DnsClientDirectory>.Instance
            )
        );
        Assert.True(await IsBlockedAsync(engine));

        scopes.Fail = true;

        // A signal makes the next evaluation try a reload; the failure keeps the last copy.
        fixture.Changed();

        Assert.True(await IsBlockedAsync(engine));
        Assert.True(await IsBlockedAsync(engine));
    }

    /// <summary>
    ///     This class wraps an <see cref="IServiceScopeFactory" />, counting the scopes opened and
    ///     optionally failing like a database that is down.
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
