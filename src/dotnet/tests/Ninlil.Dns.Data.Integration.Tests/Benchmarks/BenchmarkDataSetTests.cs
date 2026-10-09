using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Benchmarks;

/// <summary>
///     This class checks the benchmark data-set seeder (<c>Ninlil.Benchmarks</c>) on a SQLite
///     file, so it needs no Docker: seeding the small scale twice writes once (the marker zone
///     makes the second call a no-op), and the row counts equal the data-set definition.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class BenchmarkDataSetTests(SqliteEngineFixture fixture)
{
    [Fact]
    public async Task EnsureSeededAsync_CalledTwiceOnTheSmallScale_SeedsOnceWithTheDefinedRowCounts()
    {
        BenchmarkDataSetShape shape = BenchmarkDataSet.ShapeFor(BenchmarkScaleEnum.Small);
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );

        bool first = await BenchmarkDataSet.EnsureSeededAsync(
            database,
            BenchmarkScaleEnum.Small,
            TextWriter.Null
        );
        bool second = await BenchmarkDataSet.EnsureSeededAsync(
            database,
            BenchmarkScaleEnum.Small,
            TextWriter.Null
        );

        Assert.True(first);
        Assert.False(second);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        Assert.Equal(
            shape.QueryLogEntries,
            await services
                .GetRequiredService<IDnsQueryLogRepository>()
                .CountAsync(new DnsQueryLogSearchCriteria())
        );

        // The large zone, the ordinary zones, the forwarder zones and the marker.
        Assert.Equal(
            1 + shape.Zones + shape.ForwarderZones + 1,
            await services.GetRequiredService<IZoneRepository>().CountAsync()
        );
        Assert.Equal(
            shape.Leases,
            await services.GetRequiredService<ILeaseRepository>().CountAsync()
        );
        Assert.Equal(
            shape.Users,
            await services.GetRequiredService<IUserRepository>().CountAsync()
        );
        Assert.Equal(
            shape.Clients,
            await services
                .GetRequiredService<IDnsClientRepository>()
                .CountAsync(DnsClientSearchCriteria.All)
        );

        IZoneRepository zones = services.GetRequiredService<IZoneRepository>();
        Guid bigZoneId = (await zones.FindByNameAsync(BenchmarkDataSet.BigZoneName)).Id;
        Assert.Equal(
            shape.BigZoneRecords,
            await services.GetRequiredService<IRecordRepository>().CountByZoneIdAsync(bigZoneId)
        );
        Assert.Equal(
            shape.EntriesPerList,
            (
                await services
                    .GetRequiredService<IDnsFilterListEntryRepository>()
                    .ListByListIdAsync(
                        (
                            await services
                                .GetRequiredService<IDnsFilterListRepository>()
                                .ListAsync(list => list.Name == BenchmarkDataSet.FilterListName(0))
                        )[0].Id
                    )
            ).Count
        );
    }
}
