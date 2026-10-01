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
using Ninlil.Dns.Server.Acme.Caa;
using Ninlil.Dns.Server.Acme.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests.Caa;

/// <summary>
///     This class tests <see cref="CaaRecordReconciliationService" /> against SQLite-backed
///     repositories and the real <see cref="IZoneUpdateService" />, like
///     <c>AcmeDnsChallengeResponderTests</c> and <c>ZoneUpdateServiceRecordEditTests</c>. It covers
///     adding, keeping and replacing the managed record, leaving operator records alone, zone
///     eligibility, the skip cases, and the per-zone and global settings.
/// </summary>
public sealed class CaaRecordReconciliationServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-caa-reconciliation-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher, NoOpEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method builds a <see cref="CaaRecordReconciliationService" /> in a new dependency
    ///     injection scope, so it sees earlier writes the way a reconciliation started by a new
    ///     <c>DnsZoneChangedEvent</c> would.
    /// </summary>
    private CaaRecordReconciliationService CreateService(
        bool defaultEnabled = true,
        string defaultIssuerDomain = ""
    )
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new CaaRecordReconciliationService(
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            new StaticOptionsMonitor<CaaAutoManagementOptions>(
                new CaaAutoManagementOptions
                {
                    DefaultEnabled = defaultEnabled,
                    DefaultIssuerDomain = defaultIssuerDomain,
                }
            ),
            NullLogger<CaaRecordReconciliationService>.Instance
        );
    }

    /// <summary>
    ///     This method seeds a zone and its SOA record (which
    ///     <see cref="IZoneUpdateService.ApplyChangesAsync" /> needs to advance the serial) and
    ///     returns the zone identifier.
    /// </summary>
    private async Task<Guid> SeedZoneAsync(
        string name,
        ZoneTypeEnum type,
        bool? autoManageCaaRecords = null,
        string caaIssuerDomain = null
    )
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = type,
                AutoManageCaaRecords = autoManageCaaRecords,
                CaaIssuerDomain = caaIssuerDomain,
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
                TimeToLiveSeconds = 3600,
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

    private async Task<IReadOnlyList<RecordEntity>> GetCaaRecordsAsync(Guid zoneId, string name) =>
        await _recordRepository.FindByNameAsync(zoneId, name, DnsResourceRecordTypeEnum.Caa);

    [Fact]
    public async Task ReconcileAddsTheExpectedCaaRecordForAnEligiblePrimaryZone()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-add.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-add.example.com"
        );
        RecordEntity record = Assert.Single(caaRecords);
        Assert.Equal(RecordManagedByFeatureEnum.Caa, record.ManagedByFeature);

        CaaRecordData data = Assert.IsType<CaaRecordData>(
            Authoritative.Cache.RecordEntityCodec.DecodeTypedData(record)
        );
        Assert.False(data.IsCritical);
        Assert.Equal("issue", data.Tag);
        Assert.Equal("letsencrypt.org", data.Value);
    }

    [Fact]
    public async Task ReconcileUsesTheGlobalDefaultIssuerDomainWhenTheZoneHasNoneOfItsOwn()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-global-default.example.com",
            ZoneTypeEnum.CloudPrimary,
            autoManageCaaRecords: true
        );

        await CreateService(defaultIssuerDomain: "global-ca.example.net")
            .ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-global-default.example.com"
        );
        RecordEntity record = Assert.Single(caaRecords);
        CaaRecordData data = Assert.IsType<CaaRecordData>(
            Authoritative.Cache.RecordEntityCodec.DecodeTypedData(record)
        );
        Assert.Equal("global-ca.example.net", data.Value);
    }

    [Fact]
    public async Task ReconcileTwiceWithTheSameIssuerDomainDoesNotDuplicateOrChurnTheRecord()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-idempotent.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        CaaRecordReconciliationService service = CreateService();
        await service.ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> firstPass = await GetCaaRecordsAsync(
            zoneId,
            "caa-idempotent.example.com"
        );
        Guid recordId = Assert.Single(firstPass).Id;

        await service.ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> secondPass = await GetCaaRecordsAsync(
            zoneId,
            "caa-idempotent.example.com"
        );
        RecordEntity onlyRecord = Assert.Single(secondPass);
        Assert.Equal(recordId, onlyRecord.Id);
    }

    [Fact]
    public async Task ChangingTheIssuerDomainRemovesTheOldManagedRecordAndAddsANewOne()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-rotate.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "old-ca.example.net"
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> beforeRotation = await GetCaaRecordsAsync(
            zoneId,
            "caa-rotate.example.com"
        );
        Guid oldRecordId = Assert.Single(beforeRotation).Id;

        ZoneEntity zone = await _zoneRepository.GetByIdAsync(zoneId);
        zone.CaaIssuerDomain = "new-ca.example.net";
        _zoneRepository.Update(zone);
        await _unitOfWork.SaveChangesAsync();

        // A new service instance, because each instance's repositories share one DbContext for
        // its lifetime, like a per-event scope. The old instance would read its tracked copy
        // of the zone, still with the old CaaIssuerDomain, not the row just updated through
        // another context.
        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> afterRotation = await GetCaaRecordsAsync(
            zoneId,
            "caa-rotate.example.com"
        );
        RecordEntity newRecord = Assert.Single(afterRotation);
        Assert.NotEqual(oldRecordId, newRecord.Id);

        CaaRecordData data = Assert.IsType<CaaRecordData>(
            Authoritative.Cache.RecordEntityCodec.DecodeTypedData(newRecord)
        );
        Assert.Equal("new-ca.example.net", data.Value);

        // The old record must be removed, not left next to the new one.
        Assert.DoesNotContain(afterRotation, record => record.Id == oldRecordId);
    }

    [Fact]
    public async Task ReconcileNeverTouchesAnOperatorAddedCaaRecordWithNoProvenanceMarker()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-operator.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        RecordEntity operatorRecord = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = "caa-operator.example.com",
            Type = DnsResourceRecordTypeEnum.Caa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 3600,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(CaaRecordData),
                new CaaRecordData(true, "issue", "some-other-ca.example.org")
            ),
            // ManagedByFeature is left null: this is an operator's record.
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await _recordRepository.AddAsync(operatorRecord);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-operator.example.com"
        );

        // The operator's record is still there, unchanged.
        RecordEntity stillPresentOperatorRecord = Assert.Single(
            caaRecords,
            record => record.Id == operatorRecord.Id
        );
        Assert.Null(stillPresentOperatorRecord.ManagedByFeature);
        CaaRecordData operatorData = Assert.IsType<CaaRecordData>(
            Authoritative.Cache.RecordEntityCodec.DecodeTypedData(stillPresentOperatorRecord)
        );
        Assert.Equal("some-other-ca.example.org", operatorData.Value);
        Assert.True(operatorData.IsCritical);

        // ... and the managed record was added next to it.
        RecordEntity managedRecord = Assert.Single(
            caaRecords,
            record => record.ManagedByFeature == RecordManagedByFeatureEnum.Caa
        );
        CaaRecordData managedData = Assert.IsType<CaaRecordData>(
            Authoritative.Cache.RecordEntityCodec.DecodeTypedData(managedRecord)
        );
        Assert.Equal("letsencrypt.org", managedData.Value);
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Secondary)]
    [InlineData(ZoneTypeEnum.CloudSecondary)]
    [InlineData(ZoneTypeEnum.Stub)]
    [InlineData(ZoneTypeEnum.Forwarder)]
    public async Task ReconcileNeverTouchesAZoneItDoesNotOwnTheContentOfEvenWhenFlaggedEnabled(
        ZoneTypeEnum zoneType
    )
    {
        Guid zoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = "caa-ineligible.example.com",
                Type = zoneType,
                AutoManageCaaRecords = true,
                CaaIssuerDomain = "letsencrypt.org",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // No SOA record is seeded, so if an ineligible zone type ever reached IZoneUpdateService
        // the call would throw (no SOA to advance) instead of quietly writing a record.
        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> allRecords = await _recordRepository.ListByZoneIdAsync(zoneId);
        Assert.Empty(allRecords);
    }

    [Fact]
    public async Task ReconcileSkipsAZoneWithAutoManageCaaRecordsExplicitlyFalse()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-disabled.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: false,
            caaIssuerDomain: "letsencrypt.org"
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-disabled.example.com"
        );
        Assert.Empty(caaRecords);
    }

    [Fact]
    public async Task ReconcileSkipsWithoutErrorWhenNoIssuerDomainResolvesAtAll()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-no-issuer.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true
        );

        // No per-zone CaaIssuerDomain and an empty global default (the CreateService default):
        // the pass must be a logged skip, not an exception.
        await CreateService(defaultIssuerDomain: "").ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-no-issuer.example.com"
        );
        Assert.Empty(caaRecords);
    }

    [Fact]
    public async Task ReconcileDomainAsyncResolvesTheZoneByNameForTheAcmeIssuerTrigger()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-by-domain.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        await CreateService().ReconcileDomainAsync("caa-by-domain.example.com");

        IReadOnlyList<RecordEntity> caaRecords = await GetCaaRecordsAsync(
            zoneId,
            "caa-by-domain.example.com"
        );
        Assert.Single(caaRecords);
    }

    [Fact]
    public async Task ReconcileDomainAsyncForADomainThisServerDoesNotHostDoesNotThrow() =>
        await CreateService().ReconcileDomainAsync("not-hosted-here.example.org");

    [Fact]
    public async Task ReconcileZoneAsyncForAMissingZoneIdDoesNotThrow() =>
        await CreateService().ReconcileZoneAsync(Guid.NewGuid());

    [Fact]
    public async Task ReconcileZoneAsyncForAMissingZoneIdReportsSkippedWithAReason()
    {
        CaaReconciliationResult result = await CreateService().ReconcileZoneAsync(Guid.NewGuid());

        Assert.True(result.Skipped);
        Assert.NotNull(result.SkipReason);
        Assert.Equal(0, result.RecordsAdded);
        Assert.Equal(0, result.RecordsRemoved);
    }

    [Fact]
    public async Task ReconcileZoneAsyncForAnIneligibleTypeReportsSkippedWithAReason()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-skip-type.example.com",
            ZoneTypeEnum.Secondary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        CaaReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.True(result.Skipped);
        Assert.NotNull(result.SkipReason);
    }

    [Fact]
    public async Task ReconcileZoneAsyncReportsRecordsAddedForAFreshEligibleZone()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-result-add.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        CaaReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.False(result.Skipped);
        Assert.Null(result.SkipReason);
        Assert.Equal(1, result.RecordsAdded);
        Assert.Equal(0, result.RecordsRemoved);
    }

    [Fact]
    public async Task ReconcileZoneAsyncTwiceReportsNothingChangedOnTheSecondPass()
    {
        Guid zoneId = await SeedZoneAsync(
            "caa-result-nochange.example.com",
            ZoneTypeEnum.Primary,
            autoManageCaaRecords: true,
            caaIssuerDomain: "letsencrypt.org"
        );

        CaaRecordReconciliationService service = CreateService();
        await service.ReconcileZoneAsync(zoneId);
        CaaReconciliationResult secondPass = await service.ReconcileZoneAsync(zoneId);

        Assert.False(secondPass.Skipped);
        Assert.Equal(0, secondPass.RecordsAdded);
        Assert.Equal(0, secondPass.RecordsRemoved);
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Primary, true)]
    [InlineData(ZoneTypeEnum.CloudPrimary, true)]
    [InlineData(ZoneTypeEnum.Secondary, false)]
    [InlineData(ZoneTypeEnum.CloudSecondary, false)]
    [InlineData(ZoneTypeEnum.Stub, false)]
    [InlineData(ZoneTypeEnum.Forwarder, false)]
    [InlineData(ZoneTypeEnum.Catalog, false)]
    [InlineData(ZoneTypeEnum.SecondaryForwarder, false)]
    [InlineData(ZoneTypeEnum.SecondaryCatalog, false)]
    public void IsEligibleZoneTypeMatchesExactlyPrimaryAndCloudPrimary(
        ZoneTypeEnum zoneType,
        bool expected
    ) => Assert.Equal(expected, CaaRecordReconciliationService.IsEligibleZoneType(zoneType));

    /// <summary>
    ///     This class replaces <c>DnsServerEventPublisher</c> and discards every event. The tests
    ///     call <see cref="CaaRecordReconciliationService" /> directly, so the
    ///     <see cref="IDnsServerEventPublisher" /> that <see cref="IZoneUpdateService" /> needs only
    ///     has to exist.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
