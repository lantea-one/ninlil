using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="AcmeDnsChallengeResponder" /> against SQLite-backed storage
///     and an <see cref="AuthoritativeZoneCache" />, the part of the ACME support that needs
///     no CA: publishing and removing the challenge record, and refusing domains not hosted
///     here.
/// </summary>
public sealed class AcmeDnsChallengeResponderTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-acme-challenge-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private AuthoritativeZoneCache _cache;
    private Guid _zoneId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );
        builder.Services.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneChangedEventSubscriber
        >();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = "example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = "example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse("ns1.example.com"),
                        DnsName.Parse("admin.example.com"),
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
        await unitOfWork.SaveChangesAsync();

        await _host.StartAsync();

        _cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private AcmeDnsChallengeResponder CreateResponder()
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new AcmeDnsChallengeResponder(
            _cache,
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            NullLogger<AcmeDnsChallengeResponder>.Instance
        );
    }

    private async Task WaitUntilCacheHasChallengeRecordAsync(bool expected)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            CachedZone zone = _cache.FindZone(DnsName.Parse("_acme-challenge.example.com"));
            bool hasRecord =
                zone is not null && zone.NameExists(DnsName.Parse("_acme-challenge.example.com"));

            if (hasRecord == expected)
                return;

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task PublishChallengeForAHostedDomainReturnsTrue()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();

        bool published = await responder.PublishChallengeAsync(
            "example.com",
            "fake-key-authorization"
        );

        Assert.True(published);
    }

    [Fact]
    public async Task PublishChallengeForADomainThisServerDoesNotHostReturnsFalse()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();

        bool published = await responder.PublishChallengeAsync(
            "not-hosted-here.example.org",
            "fake-key-authorization"
        );

        Assert.False(published);
    }

    [Fact]
    public async Task PublishedChallengeRecordIsVisibleThroughTheCacheWithTheGivenValue()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();
        await responder.PublishChallengeAsync("example.com", "fake-key-authorization");
        await WaitUntilCacheHasChallengeRecordAsync(true);

        CachedZone zone = _cache.FindZone(DnsName.Parse("_acme-challenge.example.com"));
        IReadOnlyList<RecordEntity> records = zone.GetRecords(
            DnsName.Parse("_acme-challenge.example.com"),
            DnsResourceRecordTypeEnum.Txt
        );

        Assert.Single(records);
        Assert.Contains("fake-key-authorization", records[0].DataJson);
    }

    [Fact]
    public async Task PublishedChallengeRecordIsMarkedExcludeFromSync()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();
        await responder.PublishChallengeAsync("example.com", "fake-key-authorization");
        await WaitUntilCacheHasChallengeRecordAsync(true);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_zoneId);
        RecordEntity challengeRecord = records.Single(r =>
            r.Type == DnsResourceRecordTypeEnum.Txt && r.Name == "_acme-challenge.example.com"
        );

        Assert.True(challengeRecord.ExcludeFromSync);
    }

    [Fact]
    public async Task RemoveChallengeDeletesAPreviouslyPublishedRecord()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();
        await responder.PublishChallengeAsync("example.com", "fake-key-authorization");
        await WaitUntilCacheHasChallengeRecordAsync(true);

        await responder.RemoveChallengeAsync("example.com");
        await WaitUntilCacheHasChallengeRecordAsync(false);

        CachedZone zone = _cache.FindZone(DnsName.Parse("_acme-challenge.example.com"));
        Assert.False(zone.NameExists(DnsName.Parse("_acme-challenge.example.com")));
    }

    [Fact]
    public async Task RemoveChallengeForADomainThisServerDoesNotHostDoesNotThrow()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();

        await responder.RemoveChallengeAsync("not-hosted-here.example.org");
    }

    [Fact]
    public async Task RemoveChallengeWhenNothingWasEverPublishedIsANoOp()
    {
        AcmeDnsChallengeResponder responder = CreateResponder();

        await responder.RemoveChallengeAsync("example.com");
    }
}
