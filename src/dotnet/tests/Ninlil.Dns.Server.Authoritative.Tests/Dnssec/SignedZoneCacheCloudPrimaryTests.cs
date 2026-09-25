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
///     This class checks that <see cref="SignedZoneCache" /> signs
///     <see cref="ZoneTypeEnum.CloudPrimary" /> zones as well as <see cref="ZoneTypeEnum.Primary" />
///     ones, since this server is the source of a CloudPrimary zone's content. It uses the same
///     setup as <see cref="SignedZoneCacheEventTests" />: a host with a temporary SQLite database,
///     a generated key and the real <see cref="IZoneSigningService" />.
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

        // Overrides AddNinlilDnsServerCore's real IDnsServerEventPublisher registration --
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

        // A real, freshly generated key -- SignedZoneCache's StartAsync (triggered by
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
        // StartAsync must sign CloudPrimary zones; a zone type missing from its filter is silently
        // never signed. Same checks as SignedZoneCacheEventTests for a Primary zone.
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

        // ReloadZoneAsync has its own type check; a CloudPrimary zone must stay cached rather than
        // be removed as ineligible.
        await cache.ReloadZoneAsync(_zoneId);

        Assert.NotNull(cache.GetSignedData(_zoneId));
        Assert.Single(_publisher.Published.OfType<DnssecZoneResigningCompletedEvent>());
    }
}
