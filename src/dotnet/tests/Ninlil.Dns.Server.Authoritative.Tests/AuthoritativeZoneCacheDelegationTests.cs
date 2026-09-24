using Microsoft.EntityFrameworkCore;
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
///     This class is a regression test for a real gap: when this server hosts *both* a parent
///     zone and its delegated child as two
///     separate local zones, an NS or DS query at the delegation point used to resolve into the
///     child zone's own data (which has neither) instead of ever reaching the parent's real
///     answer -- <see cref="AuthoritativeZoneCache.FindZone" />'s "longest suffix wins" rule had
///     no notion that NS/DS at a delegation point belong to the *parent* even when a same-named
///     zone is also separately hosted. Fixed by making <see cref="AuthoritativeZoneCache.FindZone" />
///     type-aware for exactly those two types.
/// </summary>
public sealed class AuthoritativeZoneCacheDelegationTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-delegation-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private AuthoritativeZoneCache _cache;
    private Guid _parentZoneId;
    private Guid _childZoneId;

    /// <inheritdoc />
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

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _parentZoneId = Guid.NewGuid();
        _childZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _parentZoneId,
                Name = "example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _childZoneId,
                Name = "child.example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        async Task AddRecordAsync<TData>(
            Guid zoneId,
            string name,
            DnsResourceRecordTypeEnum type,
            TData data
        )
            where TData : IDnsRecordData =>
            await recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zoneId,
                    Name = name,
                    Type = type,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = 3600,
                    DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        typeof(TData),
                        data
                    ),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        await AddRecordAsync(
            _parentZoneId,
            "example.com",
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse("ns1.example.com"),
                DnsName.Parse("admin.example.com"),
                1,
                3600,
                900,
                604800,
                3600
            )
        );
        await AddRecordAsync(
            _parentZoneId,
            "example.com",
            DnsResourceRecordTypeEnum.NS,
            new NsRecordData(DnsName.Parse("ns1.example.com"))
        );
        await AddRecordAsync(
            _parentZoneId,
            "www.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.10"))
        );
        // The delegation itself: the parent's own NS records naming the child.
        await AddRecordAsync(
            _parentZoneId,
            "child.example.com",
            DnsResourceRecordTypeEnum.NS,
            new NsRecordData(DnsName.Parse("ns1.child.example.com"))
        );

        await AddRecordAsync(
            _childZoneId,
            "child.example.com",
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse("ns1.child.example.com"),
                DnsName.Parse("admin.child.example.com"),
                1,
                3600,
                900,
                604800,
                3600
            )
        );
        await AddRecordAsync(
            _childZoneId,
            "www.child.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.20"))
        );
        await unitOfWork.SaveChangesAsync();

        _cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
        await _cache.StartAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    [Fact]
    public void NsQueryAtALocallyHostedDelegationPointResolvesToTheParentZone()
    {
        CachedZone zone = _cache.FindZone(
            DnsName.Parse("child.example.com"),
            DnsResourceRecordTypeEnum.NS
        );

        Assert.Equal(_parentZoneId, zone.Zone.Id);
    }

    [Fact]
    public void DsQueryAtALocallyHostedDelegationPointResolvesToTheParentZone()
    {
        CachedZone zone = _cache.FindZone(
            DnsName.Parse("child.example.com"),
            DnsResourceRecordTypeEnum.DS
        );

        Assert.Equal(_parentZoneId, zone.Zone.Id);
    }

    [Fact]
    public void OrdinaryQueryAtTheDelegationPointStillResolvesToTheChildZoneItself()
    {
        CachedZone zone = _cache.FindZone(
            DnsName.Parse("child.example.com"),
            DnsResourceRecordTypeEnum.Soa
        );

        Assert.Equal(_childZoneId, zone.Zone.Id);
    }

    [Fact]
    public void OrdinaryQueryWithNoTypeAtAllStillResolvesToTheChildZoneItself()
    {
        CachedZone zone = _cache.FindZone(DnsName.Parse("child.example.com"));

        Assert.Equal(_childZoneId, zone.Zone.Id);
    }

    [Fact]
    public void QueryUnderneathTheDelegationResolvesToTheChildZone()
    {
        CachedZone zone = _cache.FindZone(
            DnsName.Parse("www.child.example.com"),
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(_childZoneId, zone.Zone.Id);
    }

    [Fact]
    public void OrdinaryQueryUnderTheParentResolvesToTheParentZone()
    {
        CachedZone zone = _cache.FindZone(
            DnsName.Parse("www.example.com"),
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(_parentZoneId, zone.Zone.Id);
    }
}
