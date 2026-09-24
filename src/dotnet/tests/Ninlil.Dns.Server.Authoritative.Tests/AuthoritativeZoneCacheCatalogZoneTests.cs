using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests;

/// <summary>
///     This class is a regression proof for a known bug class ("any time a new answerable
///     ZoneTypeEnum value is added, check AuthoritativeZoneCache's own zone-type filter"), exactly
///     the shape of bug
///     <see cref="ZoneTypeEnum.Catalog" />/<see cref="ZoneTypeEnum.SecondaryCatalog" /> could have
///     silently repeated -- both types are genuinely answerable/transferable zones now (RFC 9432
///     describes a catalog zone as "a regular DNS zone"), and this proves, against a real
///     SQLite-backed repository stack (not just a code read), that both
///     <see cref="AuthoritativeZoneCache.StartAsync" /> and
///     <see cref="AuthoritativeZoneCache.ReloadZoneAsync" /> genuinely load and reload them, while
///     a genuinely still-out-of-scope type (<see cref="ZoneTypeEnum.Stub" />) remains excluded.
/// </summary>
public sealed class AuthoritativeZoneCacheCatalogZoneTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-catalog-cache-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private AuthoritativeZoneCache _cache;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<AuthoritativeZoneCache>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
        _cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    private async Task<Guid> SeedZoneWithSoaAsync(string name, ZoneTypeEnum type)
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = type,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = name,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 0,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{name}"),
                        DnsName.Parse($"admin.{name}"),
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        return zoneId;
    }

    [Fact]
    public async Task StartAsyncLoadsACatalogZoneAndItIsAnswerable()
    {
        Guid zoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);

        await _cache.StartAsync(CancellationToken.None);

        CachedZone found = _cache.FindZone(DnsName.Parse("catalog.invalid"));
        Assert.NotNull(found);
        Assert.Equal(zoneId, found.Zone.Id);
    }

    [Fact]
    public async Task StartAsyncLoadsASecondaryCatalogZoneAndItIsAnswerable()
    {
        Guid zoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.SecondaryCatalog);

        await _cache.StartAsync(CancellationToken.None);

        CachedZone found = _cache.FindZone(DnsName.Parse("catalog.invalid"));
        Assert.NotNull(found);
        Assert.Equal(zoneId, found.Zone.Id);
    }

    [Fact]
    public async Task StartAsyncStillExcludesAGenuinelyOutOfScopeStubZone()
    {
        await SeedZoneWithSoaAsync("stub.invalid", ZoneTypeEnum.Stub);

        await _cache.StartAsync(CancellationToken.None);

        Assert.Null(_cache.FindZone(DnsName.Parse("stub.invalid")));
    }

    [Fact]
    public async Task ReloadZoneAsyncLoadsACatalogZoneCreatedAfterStartup()
    {
        await _cache.StartAsync(CancellationToken.None);
        Assert.Null(_cache.FindZone(DnsName.Parse("catalog.invalid")));

        Guid zoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        await _cache.ReloadZoneAsync(zoneId);

        CachedZone found = _cache.FindZone(DnsName.Parse("catalog.invalid"));
        Assert.NotNull(found);
        Assert.Equal(zoneId, found.Zone.Id);
    }

    [Fact]
    public async Task ReloadZoneAsyncLoadsASecondaryCatalogZoneCreatedAfterStartup()
    {
        await _cache.StartAsync(CancellationToken.None);

        Guid zoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.SecondaryCatalog);
        await _cache.ReloadZoneAsync(zoneId);

        CachedZone found = _cache.FindZone(DnsName.Parse("catalog.invalid"));
        Assert.NotNull(found);
        Assert.Equal(zoneId, found.Zone.Id);
    }

    [Fact]
    public async Task ReloadZoneAsyncEvictsACatalogZoneThatWasDeleted()
    {
        Guid zoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        await _cache.StartAsync(CancellationToken.None);
        Assert.NotNull(_cache.FindZone(DnsName.Parse("catalog.invalid")));

        ZoneEntity zone = await _zoneRepository.GetByIdAsync(zoneId);
        _zoneRepository.Remove(zone);
        await _unitOfWork.SaveChangesAsync();

        await _cache.ReloadZoneAsync(zoneId);

        Assert.Null(_cache.FindZone(DnsName.Parse("catalog.invalid")));
    }
}
