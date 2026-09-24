using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Tests;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CatalogSync;

/// <summary>
///     This class covers <see cref="CatalogZoneMembershipService" /> -- the RFC 9432 producer-side
///     surface -- against a real, SQLite-backed repository stack, mirroring
///     <c>PtrSyncServiceTests</c>'s own verification shape for this project's other
///     zone-reconciliation service.
/// </summary>
public sealed class CatalogZoneMembershipServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-catalog-membership-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IClusterNodeRepository _clusterNodeRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher, RecordingEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _clusterNodeRepository = _host.Services.GetRequiredService<IClusterNodeRepository>();
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
    ///     This method builds a real <see cref="CatalogZoneMembershipService" /> against a fresh
    ///     scope's own repositories -- the same "fresh instance per call" reasoning
    ///     <c>PtrSyncServiceTests.CreateService</c> already documents.
    /// </summary>
    private CatalogZoneMembershipService CreateService()
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new CatalogZoneMembershipService(
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new ClusterPeerAddressResolver(
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>(),
                NullLogger<ClusterPeerAddressResolver>.Instance
            )
        );
    }

    /// <summary>
    ///     This method reads a zone back through a brand-new scope's own, freshly-created
    ///     repository -- required, since EF Core's identity map is per scope, whenever a test wants to observe a
    ///     write <see cref="CreateService" />'s own separate scope just made: reusing this
    ///     fixture's own long-lived <c>_zoneRepository</c> field instead would silently return
    ///     that field's own stale, already-tracked identity-map copy of the same row.
    /// </summary>
    private async Task<ZoneEntity> GetZoneFreshAsync(Guid zoneId)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .GetByIdAsync(zoneId);
    }

    /// <summary>
    ///     This method lists a zone's own records back through a brand-new scope, for the same
    ///     reason <see cref="GetZoneFreshAsync" /> does.
    /// </summary>
    private async Task<IReadOnlyList<RecordEntity>> FindRecordsFreshAsync(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum? type
    )
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .FindByNameAsync(zoneId, name, type);
    }

    /// <summary>
    ///     This method lists every record in a zone back through a brand-new scope, for the same
    ///     reason <see cref="GetZoneFreshAsync" /> does.
    /// </summary>
    private async Task<IReadOnlyList<RecordEntity>> ListRecordsFreshAsync(Guid zoneId)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(zoneId);
    }

    private async Task<Guid> SeedZoneWithSoaAsync(
        string name,
        ZoneTypeEnum type,
        List<string> notifyAddresses = null
    )
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = type,
                NotifyAddresses = notifyAddresses ?? [],
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

    private async Task SeedHealthyPeerAsync(string host)
    {
        await _clusterNodeRepository.AddAsync(
            new ClusterNodeEntity
            {
                Id = Guid.NewGuid(),
                Name = $"peer-{host}",
                RemoteNodeId = Guid.NewGuid(),
                Host = host,
                Port = 443,
                TsigKeyName = "cluster-key",
                TlsCertificateThumbprint = "deadbeef",
                Status = ClusterNodeStatusEnum.Healthy,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task JoinWritesTheVersionRecordAndTheMemberPtrRecordIntoTheCatalogZone()
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(memberZoneId, catalogZoneId);

        IReadOnlyList<RecordEntity> versionRecords = await _recordRepository.FindByNameAsync(
            catalogZoneId,
            "version.catalog.invalid",
            DnsResourceRecordTypeEnum.Txt
        );
        RecordEntity versionRecord = Assert.Single(versionRecords);
        Assert.Equal(RecordManagedByFeatureEnum.Catalog, versionRecord.ManagedByFeature);
        TxtRecordData versionData = Assert.IsType<TxtRecordData>(
            RecordEntityCodec.DecodeTypedData(versionRecord)
        );
        Assert.Equal(["2"], versionData.Values);

        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        IReadOnlyList<RecordEntity> memberRecords = await FindRecordsFreshAsync(
            catalogZoneId,
            $"{uniqueLabel}.zones.catalog.invalid",
            DnsResourceRecordTypeEnum.Ptr
        );
        RecordEntity memberRecord = Assert.Single(memberRecords);
        PtrRecordData memberData = Assert.IsType<PtrRecordData>(
            RecordEntityCodec.DecodeTypedData(memberRecord)
        );
        Assert.Equal("example.com.", memberData.Target.ToString());

        ZoneEntity updatedMemberZone = await GetZoneFreshAsync(memberZoneId);
        Assert.Equal(catalogZoneId, updatedMemberZone.MemberOfCatalogZoneId);
    }

    [Fact]
    public async Task JoinDoesNotDuplicateTheVersionRecordForASecondMember()
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid firstMemberId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);
        Guid secondMemberId = await SeedZoneWithSoaAsync("example.net", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(firstMemberId, catalogZoneId);
        await CreateService().JoinAsync(secondMemberId, catalogZoneId);

        IReadOnlyList<RecordEntity> versionRecords = await _recordRepository.FindByNameAsync(
            catalogZoneId,
            "version.catalog.invalid",
            DnsResourceRecordTypeEnum.Txt
        );
        Assert.Single(versionRecords);
    }

    [Fact]
    public async Task JoinMergesEveryHealthyPeerAddressIntoBothTheCatalogAndTheMemberZonesNotifyAddresses()
    {
        await SeedHealthyPeerAsync("203.0.113.5");
        await SeedHealthyPeerAsync("203.0.113.6");

        Guid catalogZoneId = await SeedZoneWithSoaAsync(
            "catalog.invalid",
            ZoneTypeEnum.Catalog,
            notifyAddresses: ["198.51.100.9"] // an operator-added address, must survive the merge
        );
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(memberZoneId, catalogZoneId);

        ZoneEntity catalogZone = await GetZoneFreshAsync(catalogZoneId);
        Assert.Contains("203.0.113.5", catalogZone.NotifyAddresses);
        Assert.Contains("203.0.113.6", catalogZone.NotifyAddresses);
        Assert.Contains("198.51.100.9", catalogZone.NotifyAddresses);

        ZoneEntity memberZone = await GetZoneFreshAsync(memberZoneId);
        Assert.Contains("203.0.113.5", memberZone.NotifyAddresses);
        Assert.Contains("203.0.113.6", memberZone.NotifyAddresses);
    }

    [Fact]
    public async Task JoinIsIdempotentForAZoneAlreadyAMemberOfTheSameCatalog()
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(memberZoneId, catalogZoneId);
        await CreateService().JoinAsync(memberZoneId, catalogZoneId);

        IReadOnlyList<RecordEntity> catalogRecords = await _recordRepository.ListByZoneIdAsync(
            catalogZoneId
        );
        // One SOA (seeded), one version TXT, one member PTR -- never a second PTR from the
        // repeated call.
        Assert.Equal(3, catalogRecords.Count);
    }

    [Fact]
    public async Task JoinRejectsAZoneAlreadyBelongingToADifferentCatalog()
    {
        Guid firstCatalogId = await SeedZoneWithSoaAsync(
            "catalog-one.invalid",
            ZoneTypeEnum.Catalog
        );
        Guid secondCatalogId = await SeedZoneWithSoaAsync(
            "catalog-two.invalid",
            ZoneTypeEnum.Catalog
        );
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(memberZoneId, firstCatalogId);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().JoinAsync(memberZoneId, secondCatalogId)
        );
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Secondary)]
    [InlineData(ZoneTypeEnum.CloudSecondary)]
    [InlineData(ZoneTypeEnum.Stub)]
    [InlineData(ZoneTypeEnum.Forwarder)]
    public async Task JoinRejectsAnIneligibleMemberZoneType(ZoneTypeEnum memberZoneType)
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", memberZoneType);

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().JoinAsync(memberZoneId, catalogZoneId)
        );
    }

    [Fact]
    public async Task JoinRejectsACatalogTargetThatIsNotActuallyACatalogZone()
    {
        Guid notACatalogId = await SeedZoneWithSoaAsync(
            "not-a-catalog.invalid",
            ZoneTypeEnum.Primary
        );
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().JoinAsync(memberZoneId, notACatalogId)
        );
    }

    [Fact]
    public async Task JoinRejectsACatalogZoneWithNoSoaRecordYet()
    {
        Guid catalogZoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = catalogZoneId,
                Name = "empty-catalog.invalid",
                Type = ZoneTypeEnum.Catalog,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().JoinAsync(memberZoneId, catalogZoneId)
        );
    }

    [Fact]
    public async Task LeaveRemovesTheMemberPtrRecordAndClearsTheFieldOnTheMemberZone()
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);
        await CreateService().JoinAsync(memberZoneId, catalogZoneId);

        await CreateService().LeaveAsync(memberZoneId);

        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        IReadOnlyList<RecordEntity> memberRecords = await _recordRepository.FindByNameAsync(
            catalogZoneId,
            $"{uniqueLabel}.zones.catalog.invalid",
            DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Empty(memberRecords);

        ZoneEntity memberZone = await GetZoneFreshAsync(memberZoneId);
        Assert.Null(memberZone.MemberOfCatalogZoneId);

        // The version record itself stays -- the catalog zone can still have other members, and
        // even with none left, RFC 9432 says nothing about ever removing its own schema record.
        IReadOnlyList<RecordEntity> versionRecords = await _recordRepository.FindByNameAsync(
            catalogZoneId,
            "version.catalog.invalid",
            DnsResourceRecordTypeEnum.Txt
        );
        Assert.Single(versionRecords);
    }

    [Fact]
    public async Task LeaveIsANoOpForAZoneThatIsNotCurrentlyAMemberOfAnything()
    {
        Guid memberZoneId = await SeedZoneWithSoaAsync("example.com", ZoneTypeEnum.Primary);

        await CreateService().LeaveAsync(memberZoneId);

        ZoneEntity memberZone = await _zoneRepository.GetByIdAsync(memberZoneId);
        Assert.Null(memberZone.MemberOfCatalogZoneId);
    }

    [Fact]
    public async Task ListMembersReturnsEveryCurrentMemberAlphabetically()
    {
        Guid catalogZoneId = await SeedZoneWithSoaAsync("catalog.invalid", ZoneTypeEnum.Catalog);
        Guid firstMemberId = await SeedZoneWithSoaAsync("zzz.example.com", ZoneTypeEnum.Primary);
        Guid secondMemberId = await SeedZoneWithSoaAsync("aaa.example.com", ZoneTypeEnum.Primary);

        await CreateService().JoinAsync(firstMemberId, catalogZoneId);
        await CreateService().JoinAsync(secondMemberId, catalogZoneId);

        IReadOnlyList<string> members = await CreateService().ListMembersAsync(catalogZoneId);

        Assert.Equal(["aaa.example.com", "zzz.example.com"], members);
    }

    [Fact]
    public async Task ListMembersRejectsAZoneThatIsNotACatalogZoneAtAll()
    {
        Guid notACatalogId = await SeedZoneWithSoaAsync(
            "not-a-catalog.invalid",
            ZoneTypeEnum.Primary
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().ListMembersAsync(notACatalogId)
        );
    }
}
