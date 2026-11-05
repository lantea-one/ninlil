using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Cache;

/// <summary>
///     This class tests <see cref="ZoneCacheBackstopService" />: two instances are two
///     <see cref="AuthoritativeZoneCache" /> and <see cref="SignedZoneCache" /> pairs on one
///     temporary SQLite file with real repositories, and a write through the repository stands
///     for a change made on the other instance. A <see cref="TickingTimeProvider" /> drives the
///     pass timer, and a counting fake replaces the signing service so the tests can tell a zone
///     that was signed again from one that was left alone.
/// </summary>
public sealed class ZoneCacheBackstopTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zone-backstop-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly TickingTimeProvider _clock = new();
    private readonly CountingZoneSigningService _signing = new();
    private readonly RecordingEventPublisher _publisher = new();
    private ServiceProvider _provider;
    private AuthoritativeZoneCache _zoneCache;
    private SignedZoneCache _signedZoneCache;
    private ZoneCacheBackstopService _backstop;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        services.AddScoped<IZoneSigningService>(_ => _signing);

        // The detection pass opens a replica read scope, which marks this intent. Without a
        // replica configured the read still goes to the one database.
        services.AddScoped<StorageReadIntent>();
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_backstop is not null)
            await _backstop.StopAsync(CancellationToken.None);

        await _provider.DisposeAsync();
        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method adds a signed primary zone with the given A records and returns its id.
    /// </summary>
    private async Task<Guid> AddZoneAsync(string name, params string[] hostLabels)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        Guid zoneId = Guid.NewGuid();

        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    Type = ZoneTypeEnum.Primary,
                    IsDnssecSigned = true,
                }
            );

        foreach (string label in hostLabels)
            await AddRecordAsync(scope, zoneId, $"{label}.{name}", """{"Address":"192.0.2.1"}""");

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return zoneId;
    }

    /// <summary>
    ///     This method adds one A record the way a second instance would: straight through the
    ///     repository, with no event.
    /// </summary>
    private async Task AddRecordAsync(Guid zoneId, string name, string dataJson)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        await AddRecordAsync(scope, zoneId, name, dataJson);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static Task AddRecordAsync(
        AsyncServiceScope scope,
        Guid zoneId,
        string name,
        string dataJson
    ) =>
        scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ZoneId = zoneId,
                    Name = name,
                    Type = DnsResourceRecordTypeEnum.A,
                    TimeToLiveSeconds = 300,
                    DataJson = dataJson,
                }
            );

    /// <summary>
    ///     This method starts the observing instance: both caches load, then its backstop starts
    ///     with the ticking clock.
    /// </summary>
    private async Task StartObserverAsync()
    {
        IServiceScopeFactory scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        _zoneCache = new AuthoritativeZoneCache(scopeFactory);
        _signedZoneCache = new SignedZoneCache(
            scopeFactory,
            NullLogger<SignedZoneCache>.Instance,
            _publisher
        );
        await _zoneCache.StartAsync(CancellationToken.None);
        await _signedZoneCache.StartAsync(CancellationToken.None);

        _backstop = new ZoneCacheBackstopService(
            _zoneCache,
            _signedZoneCache,
            scopeFactory,
            _clock,
            NullLogger<ZoneCacheBackstopService>.Instance
        );
        await _backstop.StartAsync(CancellationToken.None);
    }

    /// <summary>
    ///     This method moves the clock past one pass interval and waits for that pass to finish.
    /// </summary>
    private async Task RunOnePassAsync()
    {
        // The service creates its timer on its background task; a tick before that is lost.
        for (int attempt = 0; attempt < 500 && _clock.TimerCount == 0; attempt++)
            await Task.Delay(20);

        long before = _backstop.CompletedPassCount;
        _clock.Advance(TimeSpan.FromSeconds(61));

        for (int attempt = 0; attempt < 500 && _backstop.CompletedPassCount == before; attempt++)
            await Task.Delay(20);

        Assert.True(_backstop.CompletedPassCount > before, "The pass did not run.");
    }

    [Fact]
    public async Task Pass_AfterAnotherInstanceAddedARecord_AnswersTheRecord()
    {
        Guid zoneId = await AddZoneAsync("a.example", "one");
        await StartObserverAsync();
        DnsName added = DnsName.Parse("two.a.example");
        Assert.Empty(_zoneCache.FindZone(added).GetRecords(added));

        await AddRecordAsync(zoneId, "two.a.example", """{"Address":"192.0.2.2"}""");
        Assert.Empty(_zoneCache.FindZone(added).GetRecords(added));

        await RunOnePassAsync();

        Assert.Single(_zoneCache.FindZone(added).GetRecords(added));
    }

    [Fact]
    public async Task Pass_ForAnUnchangedSignedZone_DoesNotSignItAgain()
    {
        await AddZoneAsync("same.example", "one");
        await AddZoneAsync("moving.example", "one");
        await StartObserverAsync();
        CachedZone before = _zoneCache.FindZone(DnsName.Parse("one.same.example"));
        int signedAtStart = _signing.CallCount;

        await RunOnePassAsync();

        Assert.Equal(signedAtStart, _signing.CallCount);
        Assert.Same(before, _zoneCache.FindZone(DnsName.Parse("one.same.example")));
    }

    [Fact]
    public async Task Pass_ForOneChangedZone_SignsOnlyThatZoneAgain()
    {
        await AddZoneAsync("same.example", "one");
        Guid movingId = await AddZoneAsync("moving.example", "one");
        await StartObserverAsync();
        int signedAtStart = _signing.CallCount;

        await AddRecordAsync(movingId, "two.moving.example", """{"Address":"192.0.2.2"}""");
        await RunOnePassAsync();

        Assert.Equal(signedAtStart + 1, _signing.CallCount);
        Assert.Equal(movingId, _signing.LastZoneId);
    }

    [Fact]
    public async Task Pass_ForAZoneWithAnUnwritableRecord_LeavesItAloneOnEveryPass()
    {
        Guid zoneId = await AddZoneAsync("bad.example", "one");
        await AddRecordAsync(zoneId, "broken.bad.example", """{"Address":null}""");
        await StartObserverAsync();
        CachedZone before = _zoneCache.FindZone(DnsName.Parse("one.bad.example"));
        int signedAtStart = _signing.CallCount;

        await RunOnePassAsync();
        await RunOnePassAsync();

        Assert.Equal(signedAtStart, _signing.CallCount);
        Assert.Same(before, _zoneCache.FindZone(DnsName.Parse("one.bad.example")));
    }

    [Fact]
    public async Task Pass_AfterAnotherInstanceDeletedTheZone_RemovesIt()
    {
        Guid zoneId = await AddZoneAsync("gone.example", "one");
        await StartObserverAsync();
        Assert.NotNull(_zoneCache.FindZone(DnsName.Parse("one.gone.example")));
        Assert.NotNull(_signedZoneCache.GetSignedData(zoneId));

        await using (AsyncServiceScope scope = _provider.CreateAsyncScope())
        {
            IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            zones.Remove(await zones.GetByIdAsync(zoneId));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await RunOnePassAsync();

        Assert.Null(_zoneCache.FindZone(DnsName.Parse("one.gone.example")));
        Assert.Null(_signedZoneCache.GetSignedData(zoneId));
    }

    [Fact]
    public async Task Pass_ThatReloadsAZone_PublishesNoZoneChangedEvent()
    {
        Guid zoneId = await AddZoneAsync("quiet.example", "one");
        await StartObserverAsync();

        await AddRecordAsync(zoneId, "two.quiet.example", """{"Address":"192.0.2.2"}""");
        int signedAtStart = _signing.CallCount;
        await RunOnePassAsync();

        // The pass reloaded the zone (guard against a test that proves nothing) ...
        Assert.Equal(signedAtStart + 1, _signing.CallCount);

        // ... and told nobody: a zone-changed event would start NOTIFY, PTR sync and webhooks
        // on an instance that did not make the change.
        Assert.DoesNotContain(_publisher.Published, published => published is DnsZoneChangedEvent);
    }
}
