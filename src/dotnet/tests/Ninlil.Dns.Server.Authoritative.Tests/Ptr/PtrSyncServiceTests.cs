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
///     This class covers <see cref="PtrSyncService" /> -- the PTR/reverse-zone sync engine --
///     against a real, SQLite-backed <see cref="IZoneUpdateService" />/repository
///     stack, the same verification shape
///     <c>Ninlil.Dns.Server.Acme.Tests.Caa.CaaRecordReconciliationServiceTests</c> already
///     uses for this project's own equivalent reconciliation service.
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
    ///     This method builds a real <see cref="PtrSyncService" /> against a fresh
    ///     dependency-injection scope's own repositories, so every test observes writes made by
    ///     a previous call the same way a fresh <c>DnsZoneChangedEvent</c>-triggered sync would --
    ///     mirroring <c>CaaRecordReconciliationServiceTests.CreateService</c>'s own reasoning
    ///     exactly (see that class's own doc comment on why a fresh instance per call matters,
    ///     the "EF Core identity-map staleness across scopes" shape).
    /// </summary>
    private PtrSyncService CreateService(bool defaultEnabled = false)
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new PtrSyncService(
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new StaticOptionsMonitor<PtrSyncOptions>(
                new PtrSyncOptions { DefaultEnabled = defaultEnabled }
            ),
            NullLogger<PtrSyncService>.Instance
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

        // The reverse zone itself must have been bootstrapped with its own SOA too.
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
        // created it) with its own SOA, before this forward zone is ever reconciled.
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
                // ManagedByFeature is deliberately left null -- an operator-authored record.
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

        // ... and this feature's own managed record was still added alongside it.
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

        // CreateService()'s own default parameter already matches PtrSyncOptions' real default
        // (DefaultEnabled = false) -- this exercises exactly that real, off-by-default posture.
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
    ///     This class stands in for the real, channel-backed <c>DnsServerEventPublisher</c> --
    ///     this test suite exercises <see cref="PtrSyncService" /> directly rather than through
    ///     the event bus, so <see cref="IZoneUpdateService" />'s own dependency on
    ///     <see cref="IDnsServerEventPublisher" /> only needs a registration that does not throw.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
