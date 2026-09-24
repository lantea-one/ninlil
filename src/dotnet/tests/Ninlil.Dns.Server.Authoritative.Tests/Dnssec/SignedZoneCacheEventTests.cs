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
using Ninlil.Dns.Server.Authoritative.Cache;
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
///     This class covers <see cref="DnssecZoneResigningCompletedEvent" />, confirming it actually
///     fires from <see cref="SignedZoneCache" />'s own real trigger point
///     -- a genuinely successful sign, computed by the real <see cref="IZoneSigningService" />
///     against a real, freshly generated DNSSEC key, not a stubbed-out signing result. This is
///     this project's first permanent test of <see cref="SignedZoneCache" /> itself; every prior
///     verification of it was a smoke test only.
/// </summary>
public sealed class SignedZoneCacheEventTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-signedzonecache-event-tests-{Guid.NewGuid()}.sqlite3"
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
                Name = "signed.example.com",
                Type = ZoneTypeEnum.Primary,
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
                Name = "signed.example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse("ns1.signed.example.com"),
                        DnsName.Parse("admin.signed.example.com"),
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
    public async Task StartingTheHostSignsTheZoneAndPublishesTheResigningCompletedEvent()
    {
        // SignedZoneCache.StartAsync (an IHostedService) runs the initial full sign
        // synchronously as part of the host's own startup, unlike a BackgroundService's
        // ExecuteAsync -- so, unlike CloudDnsZoneSyncServiceTests'/
        // AcmeCertificateRenewalServiceEventTests' own polling loops, the event is guaranteed to
        // already be published by the time _host.StartAsync() returns.
        await _host.StartAsync();

        DnssecZoneResigningCompletedEvent signedEvent = Assert.Single(
            _publisher.Published.OfType<DnssecZoneResigningCompletedEvent>()
        );
        Assert.Equal(_zoneId, signedEvent.ZoneId);
        Assert.Equal("signed.example.com", signedEvent.ZoneName);
        Assert.True(signedEvent.SignatureExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     This test covers <see cref="SignedZoneCache.ResignForScheduledExpiryAsync" /> -- the
    ///     path <see cref="DnssecResigningBackgroundService" /> takes for a genuinely proactive,
    ///     schedule-driven re-sign, confirming it both bumps this zone's own persisted SOA serial
    ///     (per this project's own confirmed decision that a resign is a real content change a
    ///     secondary must notice) and still re-signs and re-publishes
    ///     <see cref="DnssecZoneResigningCompletedEvent" /> exactly as an ordinary
    ///     <see cref="SignedZoneCache.ReloadZoneAsync" /> call already would.
    /// </summary>
    [Fact]
    public async Task ResignForScheduledExpiryAsyncBumpsTheSoaSerialAndStillPublishesTheResigningCompletedEvent()
    {
        await _host.StartAsync();
        _publisher.Published.Clear();

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();
        await cache.ResignForScheduledExpiryAsync(_zoneId);

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
            _zoneId,
            "signed.example.com",
            DnsResourceRecordTypeEnum.Soa
        );
        SoaRecordData soaData = Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(soaRecords[0])
        );

        // The seeded SOA's own serial started at 1 (see InitializeAsync above); a schedule-driven
        // resign must advance it exactly the same way an ordinary content change would.
        Assert.Equal(2u, soaData.Serial);

        DnssecZoneResigningCompletedEvent signedEvent = Assert.Single(
            _publisher.Published.OfType<DnssecZoneResigningCompletedEvent>()
        );
        Assert.Equal(_zoneId, signedEvent.ZoneId);
    }

    [Fact]
    public async Task ReloadingAnUnsignedZoneNeverPublishesTheResigningCompletedEvent()
    {
        await _host.StartAsync();
        _publisher.Published.Clear();

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(_zoneId);
        zone.IsDnssecSigned = false;
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync();

        await cache.ReloadZoneAsync(_zoneId);

        Assert.Empty(_publisher.Published.OfType<DnssecZoneResigningCompletedEvent>());
    }
}
