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
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Authoritative.Tests;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CatalogSync;

/// <summary>
///     This class covers <see cref="CatalogZoneAutoProvisioningService" /> -- the RFC 9432
///     consumer-side diff/provision/de-provision engine -- against a real, SQLite-backed
///     repository stack. Member PTR records are seeded directly into a
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone (standing in for content a real AXFR/IXFR
///     pull would have written), rather than routed through
///     <see cref="CatalogZoneMembershipService" />, since that service's own producer-side
///     <c>JoinAsync</c> only ever targets a <see cref="ZoneTypeEnum.Catalog" /> original.
/// </summary>
public sealed class CatalogZoneAutoProvisioningServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-catalog-autoprovision-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IClusterNodeRepository _clusterNodeRepository;
    private IUnitOfWork _unitOfWork;
    private RecordingEventPublisher _eventPublisher;
    private RecordingRefreshTrigger _refreshTrigger;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _clusterNodeRepository = _host.Services.GetRequiredService<IClusterNodeRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
        _eventPublisher = new RecordingEventPublisher();
        _refreshTrigger = new RecordingRefreshTrigger();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    private CatalogZoneAutoProvisioningService CreateService()
    {
        IServiceScope scope = _host.Services.CreateScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        CatalogZoneMembershipService membershipService = new(
            zoneRepository,
            recordRepository,
            new ZoneUpdateService(
                zoneRepository,
                recordRepository,
                scope.ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>(),
                unitOfWork,
                _eventPublisher,
                new ZoneWriteSerializer()
            ),
            unitOfWork,
            new ClusterPeerAddressResolver(
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>(),
                NullLogger<ClusterPeerAddressResolver>.Instance
            )
        );

        return new CatalogZoneAutoProvisioningService(
            zoneRepository,
            recordRepository,
            unitOfWork,
            _eventPublisher,
            membershipService,
            new ClusterPeerAddressResolver(
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>(),
                NullLogger<ClusterPeerAddressResolver>.Instance
            ),
            _refreshTrigger,
            NullLogger<CatalogZoneAutoProvisioningService>.Instance
        );
    }

    private async Task<Guid> SeedSecondaryCatalogZoneAsync(
        string name,
        string tsigKeyName = "cluster-key"
    )
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = ZoneTypeEnum.SecondaryCatalog,
                TsigKeyName = tsigKeyName,
                TransferTransport = ZoneTransferTransportEnum.Tls,
                PrimaryServerAddresses = ["192.0.2.1"],
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        return zoneId;
    }

    private async Task SeedMemberPtrRecordAsync(
        Guid catalogZoneId,
        string catalogZoneName,
        string memberZoneName
    )
    {
        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel(memberZoneName);

        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = catalogZoneId,
                Name = CatalogZoneRecordNames.MemberRecordOwnerName(catalogZoneName, uniqueLabel),
                Type = DnsResourceRecordTypeEnum.Ptr,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 0,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(PtrRecordData),
                    new PtrRecordData(DnsName.Parse(memberZoneName))
                ),
                ManagedByFeature = RecordManagedByFeatureEnum.Catalog,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();
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
    public async Task ReconcileProvisionsARealSecondaryZoneForEachNewlyListedMember()
    {
        await SeedHealthyPeerAsync("192.0.2.1");

        Guid catalogZoneId = await SeedSecondaryCatalogZoneAsync("catalog.invalid");
        await SeedMemberPtrRecordAsync(catalogZoneId, "catalog.invalid", "example.com");

        await CreateService().ReconcileZoneAsync(catalogZoneId);

        ZoneEntity provisioned = await _zoneRepository.FindByNameAsync("example.com");
        Assert.NotNull(provisioned);
        Assert.Equal(ZoneTypeEnum.Secondary, provisioned.Type);
        Assert.Equal(catalogZoneId, provisioned.MemberOfCatalogZoneId);
        Assert.Contains("192.0.2.1", provisioned.PrimaryServerAddresses);
        // Inherits the catalog zone's own TSIG key and transfer transport -- no separate,
        // per-member-zone provisioning an operator would otherwise have to do by hand.
        Assert.Equal("cluster-key", provisioned.TsigKeyName);
        Assert.Equal(ZoneTransferTransportEnum.Tls, provisioned.TransferTransport);

        Assert.Contains(provisioned.Id, _refreshTrigger.RequestedZoneIds);
    }

    [Fact]
    public async Task ReconcileDeprovisionsAZoneTheCatalogNoLongerLists()
    {
        await SeedHealthyPeerAsync("192.0.2.1");

        Guid catalogZoneId = await SeedSecondaryCatalogZoneAsync("catalog.invalid");
        await SeedMemberPtrRecordAsync(catalogZoneId, "catalog.invalid", "example.com");
        await CreateService().ReconcileZoneAsync(catalogZoneId);

        ZoneEntity provisioned = await _zoneRepository.FindByNameAsync("example.com");
        Assert.NotNull(provisioned);

        // The catalog no longer lists it -- remove the member PTR record, as a real IXFR/AXFR
        // diff replacing the catalog zone's own content would.
        RecordEntity memberRecord = Assert.Single(
            await _recordRepository.ListByZoneIdAsync(catalogZoneId)
        );
        _recordRepository.Remove(memberRecord);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(catalogZoneId);

        Assert.Null(await _zoneRepository.FindByNameAsync("example.com"));
        Assert.Contains(
            _eventPublisher.Published,
            @event => @event is DnsZoneChangedEvent zoneEvent && zoneEvent.ZoneId == provisioned.Id
        );
    }

    [Fact]
    public async Task ReconcileNeverOverwritesAPreexistingUnrelatedZoneWithTheSameName()
    {
        await SeedHealthyPeerAsync("192.0.2.1");

        // An operator's own, unrelated zone already exists under this exact name.
        Guid operatorZoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = operatorZoneId,
                Name = "example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        Guid catalogZoneId = await SeedSecondaryCatalogZoneAsync("catalog.invalid");
        await SeedMemberPtrRecordAsync(catalogZoneId, "catalog.invalid", "example.com");

        await CreateService().ReconcileZoneAsync(catalogZoneId);

        IReadOnlyList<ZoneEntity> allNamedExampleCom = await _zoneRepository.ListAsync(zone =>
            zone.Name == "example.com"
        );
        ZoneEntity onlyZone = Assert.Single(allNamedExampleCom);
        Assert.Equal(operatorZoneId, onlyZone.Id);
        Assert.Equal(ZoneTypeEnum.Primary, onlyZone.Type);
        Assert.Null(onlyZone.MemberOfCatalogZoneId);
    }

    [Fact]
    public async Task ReconcileIsANoOpForAZoneThatIsNotItselfASecondaryCatalog()
    {
        Guid ordinaryZoneId = Guid.NewGuid();
        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = ordinaryZoneId,
                Name = "example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await CreateService().ReconcileZoneAsync(ordinaryZoneId);

        Assert.Empty(_refreshTrigger.RequestedZoneIds);
        Assert.Empty(_eventPublisher.Published);
    }

    [Fact]
    public async Task ReconcileRunningTwiceWithNoChangeDoesNothingTheSecondTime()
    {
        await SeedHealthyPeerAsync("192.0.2.1");

        Guid catalogZoneId = await SeedSecondaryCatalogZoneAsync("catalog.invalid");
        await SeedMemberPtrRecordAsync(catalogZoneId, "catalog.invalid", "example.com");

        await CreateService().ReconcileZoneAsync(catalogZoneId);
        _refreshTrigger.RequestedZoneIds.Clear();

        await CreateService().ReconcileZoneAsync(catalogZoneId);

        Assert.Empty(_refreshTrigger.RequestedZoneIds);

        IReadOnlyList<ZoneEntity> allNamedExampleCom = await _zoneRepository.ListAsync(zone =>
            zone.Name == "example.com"
        );
        Assert.Single(allNamedExampleCom);
    }

    /// <summary>
    ///     This class stands in for the real <c>SecondaryZoneRefreshService</c>, recording every
    ///     zone identifier <see cref="ISecondaryZoneRefreshTrigger.RequestImmediateRefresh" /> was
    ///     called with instead of actually scheduling a pull.
    /// </summary>
    private sealed class RecordingRefreshTrigger : ISecondaryZoneRefreshTrigger
    {
        public List<Guid> RequestedZoneIds { get; } = [];

        public void RequestImmediateRefresh(Guid zoneId) => RequestedZoneIds.Add(zoneId);
    }
}
