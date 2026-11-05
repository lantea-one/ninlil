using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Ptr;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Ptr;

/// <summary>
///     This class tests <see cref="PtrSyncService" /> against <see cref="IZoneUpdateService" /> and the
///     EF Core repositories on a temporary SQLite database, as
///     <c>Ninlil.Dns.Server.Acme.Tests.Caa.CaaRecordReconciliationServiceTests</c> does for CAA.
/// </summary>
public sealed class PtrSyncServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-ptr-sync-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        _host = BuildHost();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
    }

    /// <summary>
    ///     This method builds one service provider over the test database file. Each call has its own
    ///     <see cref="IZoneWriteSerializer" /> and its own connections, like a separate node.
    /// </summary>
    private IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher, NoOpEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<ZoneUpdateService>();
        builder.Services.AddScoped<IZoneUpdateService, LostRaceZoneUpdateService>();
        builder.Services.AddSingleton<PtrSyncPassLock>();
        builder.Services.AddSingleton<LostRaceHook>();

        return builder.Build();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method builds a real <see cref="PtrSyncService" /> against a fresh
    ///     dependency-injection scope's repositories, so every test observes writes made by
    ///     a previous call the same way a fresh <c>DnsZoneChangedEvent</c>-triggered sync would --
    ///     mirroring <c>CaaRecordReconciliationServiceTests.CreateService</c>'s reasoning
    ///     (see that class's doc comment on why a fresh instance per call matters,
    ///     the "EF Core identity-map staleness across scopes" shape).
    /// </summary>
    private PtrSyncService CreateService(bool defaultEnabled = false, IHost host = null)
    {
        IServiceScope scope = (host ?? _host).Services.CreateScope();
        return new PtrSyncService(
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new StaticOptionsMonitor<PtrSyncOptions>(
                new PtrSyncOptions { DefaultEnabled = defaultEnabled }
            ),
            NullLogger<PtrSyncService>.Instance,
            scope.ServiceProvider.GetRequiredService<PtrSyncPassLock>(),
            (host ?? _host).Services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    private async Task<Guid> SeedForwardZoneAsync(
        string name,
        ZoneTypeEnum type,
        bool? autoManagePtrRecords = null,
        int? ptrIpv4PrefixLength = null
    )
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = type,
                AutoManagePtrRecords = autoManagePtrRecords,
                PtrIpv4PrefixLength = ptrIpv4PrefixLength,
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

    private async Task<Guid> AddARecordAsync(
        Guid zoneId,
        string name,
        IPAddress address,
        bool isDisabled = false
    )
    {
        Guid recordId = Guid.NewGuid();

        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = recordId,
                ZoneId = zoneId,
                Name = name,
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(ARecordData),
                    new ARecordData(address)
                ),
                IsDisabled = isDisabled,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        return recordId;
    }

    [Fact]
    public async Task ReconcileAutoCreatesTheReverseZoneAndAddsTheMatchingPtrRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-add.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(zoneId, "host1.ptr-add.example.com", IPAddress.Parse("192.0.2.5"));

        await CreateService().ReconcileZoneAsync(zoneId);

        ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.NotNull(reverseZone);
        Assert.Equal(ZoneTypeEnum.Primary, reverseZone.Type);

        IReadOnlyList<RecordEntity> ptrRecords = await _recordRepository.FindByNameAsync(
            reverseZone.Id,
            "5.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        RecordEntity ptrRecord = Assert.Single(ptrRecords);
        Assert.Equal(RecordManagedByFeatureEnum.Ptr, ptrRecord.ManagedByFeature);

        PtrRecordData data = Assert.IsType<PtrRecordData>(
            RecordEntityCodec.DecodeTypedData(ptrRecord)
        );
        Assert.Equal("host1.ptr-add.example.com.", data.Target.ToString());

        // The reverse zone itself must have been bootstrapped with its SOA too.
        IReadOnlyList<RecordEntity> soaRecords = await _recordRepository.FindByNameAsync(
            reverseZone.Id,
            "2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Soa
        );
        Assert.Single(soaRecords);
    }

    [Fact]
    public async Task ReconcileReusesAnAlreadyExistingLocalPrimaryReverseZoneRatherThanDuplicatingIt()
    {
        // Pre-create the reverse zone (as if a previous PTR sync run, or an operator, already
        // created it) with its SOA, before this forward zone is ever reconciled.
        Guid preexistingReverseZoneId = await SeedForwardZoneAsync(
            "2.0.192.in-addr.arpa",
            ZoneTypeEnum.Primary
        );

        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-reuse.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(zoneId, "host1.ptr-reuse.example.com", IPAddress.Parse("192.0.2.9"));

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<ZoneEntity> allZones = await _zoneRepository.ListAsync(zone =>
            zone.Name == "2.0.192.in-addr.arpa"
        );
        ZoneEntity onlyReverseZone = Assert.Single(allZones);
        Assert.Equal(preexistingReverseZoneId, onlyReverseZone.Id);

        IReadOnlyList<RecordEntity> ptrRecords = await _recordRepository.FindByNameAsync(
            preexistingReverseZoneId,
            "9.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Single(ptrRecords);
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Secondary)]
    [InlineData(ZoneTypeEnum.Stub)]
    [InlineData(ZoneTypeEnum.Forwarder)]
    public async Task ReconcileSkipsRatherThanWritingIntoAReverseZoneHostedAsAnIneligibleType(
        ZoneTypeEnum reverseZoneType
    )
    {
        Guid reverseZoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = reverseZoneId,
                Name = "2.0.192.in-addr.arpa",
                Type = reverseZoneType,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-delegated.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-delegated.example.com",
            IPAddress.Parse("192.0.2.11")
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> recordsInThatZone = await _recordRepository.ListByZoneIdAsync(
            reverseZoneId
        );
        Assert.Empty(recordsInThatZone);
    }

    [Fact]
    public async Task UpdatingTheAddressRemovesTheOldPtrAndAddsTheNewOne()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-update.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        Guid aRecordId = await AddARecordAsync(
            zoneId,
            "host1.ptr-update.example.com",
            IPAddress.Parse("192.0.2.20")
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.NotNull(
            Assert.Single(
                await _recordRepository.FindByNameAsync(
                    reverseZone.Id,
                    "20.2.0.192.in-addr.arpa",
                    DnsResourceRecordTypeEnum.Ptr
                )
            )
        );

        // Move the A record to a different address within the same /24 -- the old PTR's own
        // owner name must go away, and a new one must appear.
        RecordEntity aRecord = await _recordRepository.GetByIdAsync(aRecordId);
        aRecord.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
            typeof(ARecordData),
            new ARecordData(IPAddress.Parse("192.0.2.21"))
        );
        _recordRepository.Update(aRecord);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> oldOwnerRecords = await _recordRepository.FindByNameAsync(
            reverseZone.Id,
            "20.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Empty(oldOwnerRecords);

        IReadOnlyList<RecordEntity> newOwnerRecords = await _recordRepository.FindByNameAsync(
            reverseZone.Id,
            "21.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Single(newOwnerRecords);
    }

    [Fact]
    public async Task DeletingTheARecordRemovesTheMatchingPtrRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-delete.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        Guid aRecordId = await AddARecordAsync(
            zoneId,
            "host1.ptr-delete.example.com",
            IPAddress.Parse("192.0.2.30")
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.Single(
            await _recordRepository.FindByNameAsync(
                reverseZone.Id,
                "30.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Ptr
            )
        );

        RecordEntity aRecord = await _recordRepository.GetByIdAsync(aRecordId);
        _recordRepository.Remove(aRecord);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<RecordEntity> ptrRecordsAfterDelete = await _recordRepository.FindByNameAsync(
            reverseZone.Id,
            "30.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Empty(ptrRecordsAfterDelete);
    }

    [Fact]
    public async Task ReconcileNeverTouchesAnOperatorAddedPtrRecordWithNoProvenanceMarker()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-operator.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-operator.example.com",
            IPAddress.Parse("192.0.2.40")
        );

        // Pre-create the reverse zone with an operator-authored PTR record at a *different*
        // owner name within the same /24, no provenance marker at all.
        Guid reverseZoneId = await SeedForwardZoneAsync(
            "2.0.192.in-addr.arpa",
            ZoneTypeEnum.Primary
        );
        Guid operatorRecordId = Guid.NewGuid();
        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = operatorRecordId,
                ZoneId = reverseZoneId,
                Name = "99.2.0.192.in-addr.arpa",
                Type = DnsResourceRecordTypeEnum.Ptr,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(PtrRecordData),
                    new PtrRecordData(DnsName.Parse("manually-added.example.net"))
                ),
                // ManagedByFeature is left null -- an operator-authored record.
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(zoneId);

        RecordEntity stillPresentOperatorRecord = await _recordRepository.GetByIdAsync(
            operatorRecordId
        );
        Assert.NotNull(stillPresentOperatorRecord);
        Assert.Null(stillPresentOperatorRecord.ManagedByFeature);
        PtrRecordData operatorData = Assert.IsType<PtrRecordData>(
            RecordEntityCodec.DecodeTypedData(stillPresentOperatorRecord)
        );
        Assert.Equal("manually-added.example.net.", operatorData.Target.ToString());

        // ... and this feature's managed record was still added alongside it.
        IReadOnlyList<RecordEntity> managedRecords = await _recordRepository.FindByNameAsync(
            reverseZoneId,
            "40.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Single(managedRecords);
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Secondary)]
    [InlineData(ZoneTypeEnum.CloudSecondary)]
    [InlineData(ZoneTypeEnum.Stub)]
    [InlineData(ZoneTypeEnum.Forwarder)]
    public async Task ReconcileNeverTouchesAnIneligibleForwardZoneEvenWhenFlaggedEnabled(
        ZoneTypeEnum zoneType
    )
    {
        Guid zoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = "ptr-ineligible.example.com",
                Type = zoneType,
                AutoManagePtrRecords = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // Deliberately no SOA record seeded for this zone, and no A record either -- if this
        // service ever regresses and stops skipping an ineligible forward zone type before
        // reaching any repository write, this test would still pass trivially (there is nothing
        // to sync), so the real assertion is that no reverse zone was ever created at all.
        await CreateService(defaultEnabled: true).ReconcileZoneAsync(zoneId);

        Assert.Null(await _zoneRepository.FindByNameAsync("in-addr.arpa"));
    }

    [Fact]
    public async Task ReconcileDoesNothingWhenAutoManagePtrRecordsIsUnsetAndTheGlobalDefaultIsOff()
    {
        Guid zoneId = await SeedForwardZoneAsync("ptr-off.example.com", ZoneTypeEnum.Primary);
        await AddARecordAsync(zoneId, "host1.ptr-off.example.com", IPAddress.Parse("192.0.2.50"));

        // CreateService()'s default parameter already matches PtrSyncOptions' real default
        // (DefaultEnabled = false) -- this exercises that real, off-by-default posture.
        await CreateService().ReconcileZoneAsync(zoneId);

        Assert.Null(await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa"));
    }

    [Fact]
    public async Task ReconcileSkipsADisabledARecordEntirely()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-disabled-record.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-disabled-record.example.com",
            IPAddress.Parse("192.0.2.60"),
            isDisabled: true
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        Assert.Null(await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa"));
    }

    [Fact]
    public async Task ReconcileForAClasslessZoneCreatesBothThePtrRecordAndTheDelegationCname()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-classless-add.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true,
            ptrIpv4PrefixLength: 25
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-classless-add.example.com",
            IPAddress.Parse("192.0.2.5")
        );

        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.False(result.Skipped);
        // One PTR record in the classless child zone, one delegation CNAME in the octet-aligned
        // parent zone -- both auto-created zones counted.
        Assert.Equal(2, result.RecordsAdded);
        Assert.Equal(0, result.RecordsRemoved);
        Assert.Equal(2, result.ReverseZonesTouched);

        ZoneEntity childZone = await _zoneRepository.FindByNameAsync("0/25.2.0.192.in-addr.arpa");
        Assert.NotNull(childZone);
        Assert.Equal(ZoneTypeEnum.Primary, childZone.Type);

        IReadOnlyList<RecordEntity> ptrRecords = await _recordRepository.FindByNameAsync(
            childZone.Id,
            "5.0/25.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Ptr
        );
        RecordEntity ptrRecord = Assert.Single(ptrRecords);
        Assert.Equal(RecordManagedByFeatureEnum.Ptr, ptrRecord.ManagedByFeature);
        PtrRecordData ptrData = Assert.IsType<PtrRecordData>(
            RecordEntityCodec.DecodeTypedData(ptrRecord)
        );
        Assert.Equal("host1.ptr-classless-add.example.com.", ptrData.Target.ToString());

        ZoneEntity parentZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.NotNull(parentZone);
        Assert.Equal(ZoneTypeEnum.Primary, parentZone.Type);

        IReadOnlyList<RecordEntity> cnameRecords = await _recordRepository.FindByNameAsync(
            parentZone.Id,
            "5.2.0.192.in-addr.arpa",
            DnsResourceRecordTypeEnum.Cname
        );
        RecordEntity cnameRecord = Assert.Single(cnameRecords);
        Assert.Equal(RecordManagedByFeatureEnum.Ptr, cnameRecord.ManagedByFeature);
        CnameRecordData cnameData = Assert.IsType<CnameRecordData>(
            RecordEntityCodec.DecodeTypedData(cnameRecord)
        );
        Assert.Equal("5.0/25.2.0.192.in-addr.arpa.", cnameData.Target.ToString());
    }

    [Fact]
    public async Task RemovingTheARecordOfAClasslessZoneRemovesBothThePtrAndTheDelegationCname()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-classless-remove.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true,
            ptrIpv4PrefixLength: 26
        );
        Guid aRecordId = await AddARecordAsync(
            zoneId,
            "host1.ptr-classless-remove.example.com",
            IPAddress.Parse("192.0.2.9")
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        ZoneEntity childZone = await _zoneRepository.FindByNameAsync("0/26.2.0.192.in-addr.arpa");
        Assert.NotNull(childZone);
        Assert.Single(
            await _recordRepository.FindByNameAsync(
                childZone.Id,
                "9.0/26.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Ptr
            )
        );
        ZoneEntity parentZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.Single(
            await _recordRepository.FindByNameAsync(
                parentZone.Id,
                "9.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Cname
            )
        );

        RecordEntity aRecord = await _recordRepository.GetByIdAsync(aRecordId);
        _recordRepository.Remove(aRecord);
        await _unitOfWork.SaveChangesAsync();

        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.Equal(0, result.RecordsAdded);
        Assert.Equal(2, result.RecordsRemoved);

        Assert.Empty(
            await _recordRepository.FindByNameAsync(
                childZone.Id,
                "9.0/26.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Ptr
            )
        );
        Assert.Empty(
            await _recordRepository.FindByNameAsync(
                parentZone.Id,
                "9.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Cname
            )
        );
    }

    [Fact]
    public async Task ReconcileReusesAnAlreadyExistingParentZoneForTheDelegationCname()
    {
        // Pre-create the /24 parent zone (as if a previous non-classless PTR sync run, or an
        // operator, already created it) before this classless forward zone is ever reconciled.
        Guid preexistingParentZoneId = await SeedForwardZoneAsync(
            "2.0.192.in-addr.arpa",
            ZoneTypeEnum.Primary
        );

        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-classless-reuse.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true,
            ptrIpv4PrefixLength: 27
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-classless-reuse.example.com",
            IPAddress.Parse("192.0.2.15")
        );

        await CreateService().ReconcileZoneAsync(zoneId);

        IReadOnlyList<ZoneEntity> parentZones = await _zoneRepository.ListAsync(zone =>
            zone.Name == "2.0.192.in-addr.arpa"
        );
        ZoneEntity onlyParentZone = Assert.Single(parentZones);
        Assert.Equal(preexistingParentZoneId, onlyParentZone.Id);

        Assert.Single(
            await _recordRepository.FindByNameAsync(
                preexistingParentZoneId,
                "15.2.0.192.in-addr.arpa",
                DnsResourceRecordTypeEnum.Cname
            )
        );
    }

    [Fact]
    public async Task ReconcileForAnIneligibleForwardZoneTypeReportsSkippedWithAReason()
    {
        Guid zoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = "ptr-skip-type.example.com",
                Type = ZoneTypeEnum.Secondary,
                AutoManagePtrRecords = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.True(result.Skipped);
        Assert.NotNull(result.SkipReason);
        Assert.Equal(0, result.RecordsAdded);
        Assert.Equal(0, result.RecordsRemoved);
    }

    [Fact]
    public async Task ReconcileForADisabledZoneReportsSkippedWithAReason()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-skip-disabled.example.com",
            ZoneTypeEnum.Primary
        );

        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.True(result.Skipped);
        Assert.NotNull(result.SkipReason);
    }

    [Fact]
    public async Task ReconcileForAMissingZoneReportsSkippedWithAReason()
    {
        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(Guid.NewGuid());

        Assert.True(result.Skipped);
        Assert.NotNull(result.SkipReason);
    }

    [Fact]
    public async Task ReconcileWithNothingToChangeReportsNotSkippedWithZeroCounts()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "ptr-nochange.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(
            zoneId,
            "host1.ptr-nochange.example.com",
            IPAddress.Parse("192.0.2.70")
        );

        await CreateService().ReconcileZoneAsync(zoneId);
        PtrReconciliationResult secondPass = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.False(secondPass.Skipped);
        Assert.Null(secondPass.SkipReason);
        Assert.Equal(0, secondPass.RecordsAdded);
        Assert.Equal(0, secondPass.RecordsRemoved);
    }

    /// <summary>
    ///     This test starts several passes for one forward zone at the same moment, the way a
    ///     <c>DnsZoneChangedEvent</c> pass and <c>POST /api/zones/{id}/reconcile-ptr</c> do, and
    ///     repeats the round on a fresh zone each time. Each pass must see the others' writes, so
    ///     every address ends with exactly one PTR record and the reverse zone exists once. With
    ///     <paramref name="reverseZoneExists" /> the reverse zone is created by an earlier pass, so
    ///     the passes race only on the records; without it they race on the zone too.
    /// </summary>
    /// <param name="reverseZoneExists">Whether a first pass creates the reverse zone before the race.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentPassesForOneForwardZoneStoreEachPtrRecordOnce(
        bool reverseZoneExists
    )
    {
        const int rounds = 8;
        const int passesPerRound = 3;
        const int addressesPerZone = 4;

        for (int round = 0; round < rounds; round++)
        {
            string zoneName = $"ptr-race-{round}.example.com";
            Guid zoneId = await SeedForwardZoneAsync(
                zoneName,
                ZoneTypeEnum.Primary,
                autoManagePtrRecords: true
            );
            for (int host = 1; host <= addressesPerZone; host++)
            {
                await AddARecordAsync(
                    zoneId,
                    $"host{host}.{zoneName}",
                    IPAddress.Parse($"198.51.{100 + round}.{host}")
                );

                // One pass after the first address makes the reverse zone exist before the race.
                if (reverseZoneExists && host == 1)
                    await CreateService().ReconcileZoneAsync(zoneId);
            }

            using ManualResetEventSlim start = new(false);
            Task[] passes =
            [
                .. Enumerable
                    .Range(0, passesPerRound)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            PtrSyncService service = CreateService();
                            start.Wait();
                            await service.ReconcileZoneAsync(zoneId);
                        })
                    ),
            ];
            start.Set();
            await Task.WhenAll(passes);

            ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync(
                $"{100 + round}.51.198.in-addr.arpa"
            );
            Assert.NotNull(reverseZone);

            IReadOnlyList<RecordEntity> ptrRecords = (
                await _recordRepository.ListByZoneIdAsync(reverseZone.Id)
            )
                .Where(record => record.Type == DnsResourceRecordTypeEnum.Ptr)
                .ToList();
            Assert.Equal(addressesPerZone, ptrRecords.Count);
            Assert.Equal(
                addressesPerZone,
                ptrRecords.Select(record => record.Name).Distinct().Count()
            );
        }
    }

    /// <summary>
    ///     This test runs two services on separate service providers over one SQLite file, the way
    ///     two cluster nodes share a database. The per-process pass lock and the per-zone write lock
    ///     then cover nothing, so the passes race on the records. After the race every address must
    ///     have exactly one PTR record, because each pass removes the duplicates it finds at its end.
    /// </summary>
    [Fact]
    public async Task ReconcileZoneAsync_OnTwoServiceProvidersAtOnce_StoresEachPtrRecordOnce()
    {
        const int rounds = 8;
        const int passesPerNode = 3;
        const int addressesPerZone = 4;

        using IHost secondHost = BuildHost();

        for (int round = 0; round < rounds; round++)
        {
            string zoneName = $"ptr-nodes-{round}.example.com";
            Guid zoneId = await SeedForwardZoneAsync(
                zoneName,
                ZoneTypeEnum.Primary,
                autoManagePtrRecords: true
            );
            for (int host = 1; host <= addressesPerZone; host++)
                await AddARecordAsync(
                    zoneId,
                    $"host{host}.{zoneName}",
                    IPAddress.Parse($"203.0.{100 + round}.{host}")
                );

            // The reverse zone exists first, so the nodes race on the records, not on the zone.
            await CreateService().ReconcileZoneAsync(zoneId);
            IReadOnlyList<RecordEntity> existing = await _recordRepository.ListByZoneIdAsync(
                (await _zoneRepository.FindByNameAsync($"{100 + round}.0.203.in-addr.arpa")).Id
            );
            Assert.Equal(
                addressesPerZone,
                existing.Count(r => r.Type == DnsResourceRecordTypeEnum.Ptr)
            );

            // Remove the PTRs again, so every pass starts from the same empty state and all of them
            // want to add the same records.
            await RemoveManagedPtrsAsync($"{100 + round}.0.203.in-addr.arpa");

            using ManualResetEventSlim start = new(false);
            Task[] passes =
            [
                .. Enumerable
                    .Range(0, passesPerNode * 2)
                    .Select(index =>
                        Task.Run(async () =>
                        {
                            PtrSyncService service = CreateService(
                                host: index % 2 == 0 ? _host : secondHost
                            );
                            start.Wait();
                            await service.ReconcileZoneAsync(zoneId);
                        })
                    ),
            ];
            start.Set();
            await Task.WhenAll(passes);

            ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync(
                $"{100 + round}.0.203.in-addr.arpa"
            );
            List<RecordEntity> ptrRecords =
            [
                .. (await _recordRepository.ListByZoneIdAsync(reverseZone.Id)).Where(record =>
                    record.Type == DnsResourceRecordTypeEnum.Ptr
                ),
            ];
            Assert.Equal(addressesPerZone, ptrRecords.Count);
            Assert.Equal(
                addressesPerZone,
                ptrRecords.Select(record => record.Name).Distinct().Count()
            );
        }
    }

    /// <summary>
    ///     This test makes the removal in the first reverse zone lose a race: another writer deletes
    ///     the duplicate just before the service saves, so the save fails. The duplicate removal in
    ///     the second reverse zone must still happen, because each zone is cleaned in its own scope
    ///     and the failed save leaves nothing staged for the next zone.
    /// </summary>
    [Fact]
    public async Task ReconcileZoneAsync_WhenTheFirstZonesRemovalLosesARace_StillRemovesLaterZonesDuplicates()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "lost-race.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(zoneId, "a.lost-race.example.com", IPAddress.Parse("198.18.30.1"));
        await AddARecordAsync(zoneId, "b.lost-race.example.com", IPAddress.Parse("198.18.31.1"));
        await CreateService().ReconcileZoneAsync(zoneId);

        List<Guid> reverseZoneIds = [];
        foreach (string name in new[] { "30.18.198.in-addr.arpa", "31.18.198.in-addr.arpa" })
        {
            ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync(name);
            reverseZoneIds.Add(reverseZone.Id);
            RecordEntity managed = (
                await _recordRepository.ListByZoneIdAsync(reverseZone.Id)
            ).Single(record => record.Type == DnsResourceRecordTypeEnum.Ptr);
            await _recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = reverseZone.Id,
                    Name = managed.Name,
                    Type = DnsResourceRecordTypeEnum.Ptr,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = 3600,
                    DataJson = managed.DataJson,
                    ManagedByFeature = RecordManagedByFeatureEnum.Ptr,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        }
        await _unitOfWork.SaveChangesAsync();

        _host.Services.GetRequiredService<LostRaceHook>().ArmFor(reverseZoneIds[0]);

        await CreateService().ReconcileZoneAsync(zoneId);

        foreach (Guid reverseZoneId in reverseZoneIds)
            Assert.Equal(
                1,
                (await _recordRepository.ListByZoneIdAsync(reverseZoneId)).Count(record =>
                    record.Type == DnsResourceRecordTypeEnum.Ptr
                )
            );
    }

    /// <summary>
    ///     This method deletes every PTR record of a zone, bypassing the zone update service.
    /// </summary>
    private async Task RemoveManagedPtrsAsync(string zoneName)
    {
        ZoneEntity zone = await _zoneRepository.FindByNameAsync(zoneName);
        foreach (
            RecordEntity record in (await _recordRepository.ListByZoneIdAsync(zone.Id)).Where(r =>
                r.Type == DnsResourceRecordTypeEnum.Ptr
            )
        )
            _recordRepository.Remove(record);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This test checks the duplicate rule: of two managed PTRs with the same owner and target
    ///     one is removed, while an operator-created PTR with the same owner and target (no
    ///     managed marker) stays, because the service only manages what it created.
    /// </summary>
    [Fact]
    public async Task ReconcileZoneAsync_WithAManagedDuplicate_RemovesItAndKeepsOperatorPtrs()
    {
        Guid zoneId = await SeedForwardZoneAsync(
            "dupe.example.com",
            ZoneTypeEnum.Primary,
            autoManagePtrRecords: true
        );
        await AddARecordAsync(zoneId, "host.dupe.example.com", IPAddress.Parse("198.18.7.9"));
        await CreateService().ReconcileZoneAsync(zoneId);

        ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync("7.18.198.in-addr.arpa");
        RecordEntity managed = (await _recordRepository.ListByZoneIdAsync(reverseZone.Id)).Single(
            record => record.Type == DnsResourceRecordTypeEnum.Ptr
        );

        foreach (
            RecordManagedByFeatureEnum? marker in new RecordManagedByFeatureEnum?[]
            {
                RecordManagedByFeatureEnum.Ptr,
                null,
            }
        )
        {
            await _recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = reverseZone.Id,
                    Name = managed.Name,
                    Type = DnsResourceRecordTypeEnum.Ptr,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = 3600,
                    DataJson = managed.DataJson,
                    ManagedByFeature = marker,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        }
        await _unitOfWork.SaveChangesAsync();

        PtrReconciliationResult result = await CreateService().ReconcileZoneAsync(zoneId);

        Assert.Equal(1, result.RecordsRemoved);
        List<RecordEntity> ptrs =
        [
            .. (await _recordRepository.ListByZoneIdAsync(reverseZone.Id)).Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Ptr
            ),
        ];
        Assert.Equal(2, ptrs.Count);
        Assert.Single(ptrs, record => record.ManagedByFeature == RecordManagedByFeatureEnum.Ptr);
        Assert.Single(ptrs, record => record.ManagedByFeature == null);
    }

    [Fact]
    public void IsEligibleZoneTypeMatchesExactlyPrimaryAndCloudPrimary()
    {
        Assert.True(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.Primary));
        Assert.True(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.CloudPrimary));
        Assert.False(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.Secondary));
        Assert.False(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.CloudSecondary));
        Assert.False(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.Stub));
        Assert.False(PtrSyncService.IsEligibleZoneType(ZoneTypeEnum.Forwarder));
    }

    /// <summary>
    ///     This class tells <see cref="LostRaceZoneUpdateService" /> which zone's next removal must
    ///     lose a race. It is a singleton per test host.
    /// </summary>
    private sealed class LostRaceHook
    {
        /// <summary>
        ///     This property contains the zone whose next removal loses the race, or
        ///     <see langword="null" /> when no race is armed.
        /// </summary>
        public Guid? ZoneId { get; private set; }

        /// <summary>
        ///     This method arms the hook for one removal in <paramref name="zoneId" />.
        /// </summary>
        public void ArmFor(Guid zoneId) => ZoneId = zoneId;

        /// <summary>
        ///     This method disarms the hook, so the race happens once.
        /// </summary>
        public void Disarm() => ZoneId = null;
    }

    /// <summary>
    ///     This class wraps <see cref="ZoneUpdateService" />. While <see cref="LostRaceHook" /> is
    ///     armed for a zone, it first deletes the records about to be removed through a separate
    ///     scope, as another node would, so the wrapped save finds nothing to delete and fails.
    /// </summary>
    private sealed class LostRaceZoneUpdateService(
        ZoneUpdateService inner,
        LostRaceHook hook,
        IServiceScopeFactory scopeFactory
    ) : IZoneUpdateService
    {
        public async Task ApplyChangesAsync(
            Guid zoneId,
            IReadOnlyList<RecordEntity> recordsToAdd,
            IReadOnlyList<RecordEntity> recordsToRemove,
            uint? explicitNewSerial = null,
            CancellationToken stoppingToken = default,
            DnsResourceRecord replacementSoa = null
        )
        {
            if (hook.ZoneId == zoneId && recordsToRemove.Count > 0)
            {
                hook.Disarm();
                using IServiceScope scope = scopeFactory.CreateScope();
                IRecordRepository records =
                    scope.ServiceProvider.GetRequiredService<IRecordRepository>();
                foreach (RecordEntity record in recordsToRemove)
                    records.Remove(await records.GetByIdAsync(record.Id, stoppingToken));
                await scope
                    .ServiceProvider.GetRequiredService<IUnitOfWork>()
                    .SaveChangesAsync(stoppingToken);
            }

            await inner.ApplyChangesAsync(
                zoneId,
                recordsToAdd,
                recordsToRemove,
                explicitNewSerial,
                stoppingToken,
                replacementSoa
            );
        }

        public Task BootstrapZoneAsync(
            Guid zoneId,
            IReadOnlyList<RecordEntity> records,
            CancellationToken stoppingToken = default
        ) => inner.BootstrapZoneAsync(zoneId, records, stoppingToken);

        public Task ApplyRecordEditAsync(
            Guid zoneId,
            Guid recordId,
            Action<RecordEntity> applyEdit,
            CancellationToken stoppingToken = default
        ) => inner.ApplyRecordEditAsync(zoneId, recordId, applyEdit, stoppingToken);

        public Task BumpSerialForDnssecResignAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) => inner.BumpSerialForDnssecResignAsync(zoneId, stoppingToken);
    }

    /// <summary>
    ///     This class stands in for the real, channel-backed <c>DnsServerEventPublisher</c> --
    ///     this test suite exercises <see cref="PtrSyncService" /> directly rather than through
    ///     the event bus, so <see cref="IZoneUpdateService" />'s dependency on
    ///     <see cref="IDnsServerEventPublisher" /> only needs a registration that does not throw.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
