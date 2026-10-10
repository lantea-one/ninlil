using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the <see cref="TimeSpan" /> round-trip tests that run against every
///     engine, one subclass per engine. SQL Server would map a <see cref="TimeSpan" /> to
///     <c>time</c>, which holds less than 24 hours, so each test saves spans of 1 day and 7 days
///     (the DHCP scope defaults and a daily filter list refresh) and reads them back exactly.
/// </summary>
public abstract class TimeSpanRoundTripTestsBase(EngineFixtureBase fixture)
{
    private static readonly TimeSpan Odd = new(7, 3, 4, 5, 678, 901);

    /// <summary>
    ///     A scope saved with a 7-day maximum and 1-day default lease duration reads back equal,
    ///     including the model defaults and a span with sub-second ticks.
    /// </summary>
    [Fact]
    public async Task DhcpScope_LeaseDurationsOfOneAndSevenDays_RoundTripExactly()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            IDhcpScopeRepository scopes =
                seed.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
            await scopes.AddAsync(NewScope("defaults", null, null));
            await scopes.AddAsync(NewScope("long", TimeSpan.FromDays(1), TimeSpan.FromDays(7)));
            await scopes.AddAsync(NewScope("odd", TimeSpan.FromMinutes(1), Odd));
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        DhcpScopeEntity defaults = await repository.FindByNameAsync("defaults");
        Assert.Equal(TimeSpan.FromHours(24), defaults.DefaultLeaseDuration);
        Assert.Equal(TimeSpan.FromDays(7), defaults.MaxLeaseDuration);

        DhcpScopeEntity longer = await repository.FindByNameAsync("long");
        Assert.Equal(TimeSpan.FromDays(1), longer.DefaultLeaseDuration);
        Assert.Equal(TimeSpan.FromDays(7), longer.MaxLeaseDuration);

        DhcpScopeEntity odd = await repository.FindByNameAsync("odd");
        Assert.Equal(TimeSpan.FromMinutes(1), odd.DefaultLeaseDuration);
        Assert.Equal(Odd, odd.MaxLeaseDuration);
    }

    /// <summary>
    ///     A filter list refresh interval of 1 day and 7 days reads back equal, and a list with no
    ///     interval keeps null.
    /// </summary>
    [Fact]
    public async Task FilterList_RefreshIntervalOfOneAndSevenDays_RoundTripsExactly()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            IDnsFilterListRepository lists =
                seed.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
            await lists.AddAsync(NewList("daily", TimeSpan.FromDays(1)));
            await lists.AddAsync(NewList("weekly", TimeSpan.FromDays(7)));
            await lists.AddAsync(NewList("odd", Odd));
            await lists.AddAsync(NewList("never", null));
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        Assert.Equal(
            TimeSpan.FromDays(1),
            (await repository.FindByNameAsync("daily")).RefreshInterval
        );
        Assert.Equal(
            TimeSpan.FromDays(7),
            (await repository.FindByNameAsync("weekly")).RefreshInterval
        );
        Assert.Equal(Odd, (await repository.FindByNameAsync("odd")).RefreshInterval);
        Assert.Null((await repository.FindByNameAsync("never")).RefreshInterval);
    }

    /// <summary>
    ///     A zone cloud sync interval of 1 day and 7 days reads back equal, and a zone that keeps
    ///     the model default reads back 5 minutes.
    /// </summary>
    [Fact]
    public async Task Zone_CloudSyncIntervalOfOneAndSevenDays_RoundTripsExactly()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            IZoneRepository zones = seed.ServiceProvider.GetRequiredService<IZoneRepository>();
            await zones.AddAsync(NewZone("default.example", null));
            await zones.AddAsync(NewZone("daily.example", TimeSpan.FromDays(1)));
            await zones.AddAsync(NewZone("weekly.example", TimeSpan.FromDays(7)));
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            (await repository.FindByNameAsync("default.example")).CloudSyncInterval
        );
        Assert.Equal(
            TimeSpan.FromDays(1),
            (await repository.FindByNameAsync("daily.example")).CloudSyncInterval
        );
        Assert.Equal(
            TimeSpan.FromDays(7),
            (await repository.FindByNameAsync("weekly.example")).CloudSyncInterval
        );
    }

    private static DhcpScopeEntity NewScope(string name, TimeSpan? defaultLease, TimeSpan? maxLease)
    {
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = "192.0.2.10",
            RangeEndAddress = "192.0.2.200",
            SubnetPrefixLength = 24,
        };

        // A null leaves the entity's own default (24 hours and 7 days) in place.
        if (defaultLease.HasValue)
            scope.DefaultLeaseDuration = defaultLease.Value;

        if (maxLease.HasValue)
            scope.MaxLeaseDuration = maxLease.Value;

        return scope;
    }

    private static DnsFilterListEntity NewList(string name, TimeSpan? refreshInterval) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Subscribed,
            SubscriptionUrl = "https://lists.example/list.txt",
            RefreshInterval = refreshInterval,
        };

    private static ZoneEntity NewZone(string name, TimeSpan? syncInterval)
    {
        ZoneEntity zone = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            Type = ZoneTypeEnum.Primary,
        };

        if (syncInterval.HasValue)
            zone.CloudSyncInterval = syncInterval.Value;

        return zone;
    }
}

/// <summary>This class runs <see cref="TimeSpanRoundTripTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteTimeSpanRoundTripTests(SqliteEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);

/// <summary>This class runs <see cref="TimeSpanRoundTripTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlTimeSpanRoundTripTests(PostgreSqlEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);

/// <summary>This class runs <see cref="TimeSpanRoundTripTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlTimeSpanRoundTripTests(MySqlEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);

/// <summary>This class runs <see cref="TimeSpanRoundTripTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerTimeSpanRoundTripTests(SqlServerEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);

/// <summary>
///     This class runs <see cref="TimeSpanRoundTripTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbTimeSpanRoundTripTests(MongoDbEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);

/// <summary>
///     This class runs <see cref="TimeSpanRoundTripTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneTimeSpanRoundTripTests(MongoDbStandaloneEngineFixture fixture)
    : TimeSpanRoundTripTestsBase(fixture);
