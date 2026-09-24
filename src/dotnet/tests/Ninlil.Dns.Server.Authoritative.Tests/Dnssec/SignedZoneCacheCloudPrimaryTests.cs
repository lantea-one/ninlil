using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class is the regression coverage for the backend gap audit's finding 4:
///     <see cref="SignedZoneCache" /> previously only ever DNSSEC-signed <see cref="ZoneTypeEnum.Primary" />
///     zones, silently skipping <see cref="ZoneTypeEnum.CloudPrimary" /> ones even though this
///     instance is equally the authoritative source of a CloudPrimary zone's content -- nothing
///     signs it upstream first, unlike a Secondary/CloudSecondary zone whose signed content
///     (RRSIGs included) always arrives pre-signed via NOTIFY/AXFR. Mirrors
///     <see cref="SignedZoneCacheEventTests" />'s own real-host, real-key setup exactly, just
///     against a <see cref="ZoneTypeEnum.CloudPrimary" /> zone instead of a
///     <see cref="ZoneTypeEnum.Primary" /> one, so it proves the fix against the real
///     <see cref="IZoneSigningService" /> pipeline rather than a stubbed-out one.
/// </summary>
public sealed class SignedZoneCacheCloudPrimaryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-signedzonecache-cloudprimary-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private RecordingEventPublisher _publisher;
    private Guid _zoneId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Overrides AddNinlilDnsServerCore's own real IDnsServerEventPublisher registration --
        // this test only cares about capturing what gets published directly.
        _publisher = new RecordingEventPublisher();
        builder.Services.AddSingleton<IDnsServerEventPublisher>(_publisher);

        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            seedScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

        _zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = "signed-cloud.example.com",
                Type = ZoneTypeEnum.CloudPrimary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = "signed-cloud.example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse("ns1.signed-cloud.example.com"),
                        DnsName.Parse("admin.signed-cloud.example.com"),
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

        // A real, freshly generated key -- SignedZoneCache's own StartAsync (triggered by
        // _host.StartAsync() below) needs at least one active key of each role to actually
        // produce a signed result rather than logging a failure and skipping the zone.
        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task StartingTheHostSignsACloudPrimaryZoneAndPublishesTheResigningCompletedEvent()
    {
        // Before the fix, SignedZoneCache.StartAsync's zone-type filter only ever matched
        // ZoneTypeEnum.Primary, so a CloudPrimary zone -- this instance's own authoritative
        // source for content it pushes out to a cloud connector -- would silently never be
        // signed at all, with no error anywhere. This proves the fix by asserting the same
        // real-signing, real-event-publishing behavior SignedZoneCacheEventTests already proves
        // for a Primary zone, here against a CloudPrimary one instead.
        await _host.StartAsync();

        DnssecZoneResigningCompletedEvent signedEvent = Assert.Single(
            _publisher.Published.OfType<DnssecZoneResigningCompletedEvent>()
        );
        Assert.Equal(_zoneId, signedEvent.ZoneId);
        Assert.Equal("signed-cloud.example.com", signedEvent.ZoneName);
        Assert.True(signedEvent.SignatureExpiresAtUtc > DateTimeOffset.UtcNow);

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();
        Assert.NotNull(cache.GetSignedData(_zoneId));
    }

    [Fact]
    public async Task ReloadingAStillSignedCloudPrimaryZoneKeepsItInTheCache()
    {
        await _host.StartAsync();
        _publisher.Published.Clear();

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();

        // Before the fix, ReloadZoneAsync's own zone-type check independently only matched
        // ZoneTypeEnum.Primary too -- a reload of an unchanged CloudPrimary zone would have
        // fallen into the else-branch removal path and evicted it from the cache entirely, even
        // though nothing about the zone actually became ineligible.
        await cache.ReloadZoneAsync(_zoneId);

        Assert.NotNull(cache.GetSignedData(_zoneId));
        Assert.Single(_publisher.Published.OfType<DnssecZoneResigningCompletedEvent>());
    }
}
