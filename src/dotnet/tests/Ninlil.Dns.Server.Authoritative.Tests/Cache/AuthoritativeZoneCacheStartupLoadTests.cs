using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Cache;

/// <summary>
///     This class tests the startup load of <see cref="AuthoritativeZoneCache" /> against a
///     temporary SQLite database with real repositories: every answerable zone is cached with its
///     own records, zones that are disabled or of a type the cache does not hold are left out,
///     and the number of queries does not grow with the number of zones.
/// </summary>
public sealed class AuthoritativeZoneCacheStartupLoadTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zone-cache-startup-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly CommandCounter _commands = new();
    private readonly ServiceProvider _provider;

    public AuthoritativeZoneCacheStartupLoadTests()
    {
        ServiceCollection services = new();
        services.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}").AddInterceptors(_commands)
        );
        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method adds a zone with the given number of A records at <c>host-N.zone</c>.
    /// </summary>
    private async Task AddZoneAsync(
        string name,
        ZoneTypeEnum type,
        int recordCount,
        bool isDisabled = false
    )
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        Guid zoneId = Guid.NewGuid();
        await zones.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Name = name,
                Type = type,
                IsDisabled = isDisabled,
            }
        );

        for (int index = 0; index < recordCount; index++)
            await records.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ZoneId = zoneId,
                    Name = $"host-{index}.{name}",
                    Type = DnsResourceRecordTypeEnum.A,
                    TimeToLiveSeconds = 300,
                    DataJson = """{"Address":"192.0.2.1"}""",
                }
            );

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task StartAsync_LoadsEveryAnswerableZoneWithOnlyItsOwnRecords()
    {
        await AddZoneAsync("one.example", ZoneTypeEnum.Primary, 3);
        await AddZoneAsync("two.example", ZoneTypeEnum.Secondary, 2);
        await AddZoneAsync("empty.example", ZoneTypeEnum.Primary, 0);
        await AddZoneAsync("off.example", ZoneTypeEnum.Primary, 4, isDisabled: true);
        await AddZoneAsync("stub.example", ZoneTypeEnum.Stub, 4);

        AuthoritativeZoneCache cache = new(_provider.GetRequiredService<IServiceScopeFactory>());
        await cache.StartAsync(CancellationToken.None);

        CachedZone one = cache.FindZone(DnsName.Parse("host-0.one.example"));
        CachedZone two = cache.FindZone(DnsName.Parse("host-0.two.example"));
        CachedZone empty = cache.FindZone(DnsName.Parse("www.empty.example"));

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotNull(empty);
        Assert.Null(cache.FindZone(DnsName.Parse("host-0.off.example")));
        Assert.Null(cache.FindZone(DnsName.Parse("host-0.stub.example")));

        for (int index = 0; index < 3; index++)
            Assert.Single(one.GetRecords(DnsName.Parse($"host-{index}.one.example")));

        Assert.Empty(one.GetRecords(DnsName.Parse("host-0.two.example")));
        Assert.Single(two.GetRecords(DnsName.Parse("host-1.two.example")));
        Assert.Empty(two.GetRecords(DnsName.Parse("host-2.two.example")));
        Assert.Empty(empty.GetRecords(DnsName.Parse("host-0.empty.example")));
    }

    [Fact]
    public async Task StartAsync_ForManyZones_RunsTheSameNumberOfQueriesAsForOne()
    {
        await AddZoneAsync("first.example", ZoneTypeEnum.Primary, 2);

        AuthoritativeZoneCache single = new(_provider.GetRequiredService<IServiceScopeFactory>());
        int before = _commands.Count;
        await single.StartAsync(CancellationToken.None);
        int queriesForOneZone = _commands.Count - before;

        for (int index = 0; index < 30; index++)
            await AddZoneAsync($"zone-{index}.example", ZoneTypeEnum.Primary, 2);

        AuthoritativeZoneCache many = new(_provider.GetRequiredService<IServiceScopeFactory>());
        before = _commands.Count;
        await many.StartAsync(CancellationToken.None);

        Assert.Equal(queriesForOneZone, _commands.Count - before);
        Assert.Single(
            many.FindZone(DnsName.Parse("host-1.zone-29.example"))
                .GetRecords(DnsName.Parse("host-1.zone-29.example"))
        );
    }
}
