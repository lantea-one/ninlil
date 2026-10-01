using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpDnsRegistrationService" />: registering, updating and
///     removing records, the skip cases, and leaving unmanaged records alone. It calls the
///     service directly on <see cref="IZoneUpdateService" /> and the repositories over a temporary
///     SQLite file, like <c>Ninlil.Dns.Server.Authoritative.Tests.Ptr.PtrSyncServiceTests</c>.
/// </summary>
public sealed class DhcpDnsRegistrationServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-dns-integration-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IDhcpScopeRepository _scopeRepository;
    private ILeaseRepository _leaseRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();
        builder.Services.AddSingleton<IDnsServerEventPublisher, NoOpEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _scopeRepository = _host.Services.GetRequiredService<IDhcpScopeRepository>();
        _leaseRepository = _host.Services.GetRequiredService<ILeaseRepository>();
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
    ///     This method builds a <see cref="DhcpDnsRegistrationService" /> on the repositories of a
    ///     new dependency injection scope for each call, as <c>PtrSyncServiceTests.CreateService</c>
    ///     does, so no tracked entity from an earlier step is reused.
    /// </summary>
    private DhcpDnsRegistrationService CreateService()
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new DhcpDnsRegistrationService(
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>(),
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            NullLogger<DhcpDnsRegistrationService>.Instance
        );
    }

    private async Task<Guid> SeedForwardZoneAsync(
        string name,
        ZoneTypeEnum type = ZoneTypeEnum.Primary
    )
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = type,
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

    private async Task<DhcpScopeEntity> SeedDhcpScopeAsync(string name, Guid? dnsZoneId)
    {
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = "192.0.2.10",
            RangeEndAddress = "192.0.2.20",
            SubnetPrefixLength = 24,
            DnsZoneId = dnsZoneId,
        };

        await _scopeRepository.AddAsync(scope);
        await _unitOfWork.SaveChangesAsync();

        return scope;
    }

    private async Task<LeaseEntity> SeedLeaseAsync(
        string scopeName,
        string ipAddress,
        string hostName,
        LeaseStateEnum state = LeaseStateEnum.Active
    )
    {
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scopeName,
            ClientIdentifier = "aa:bb:cc:dd:ee:ff",
            IpAddress = ipAddress,
            HostName = hostName,
            State = state,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        };

        await _leaseRepository.AddAsync(lease);
        await _unitOfWork.SaveChangesAsync();

        return lease;
    }

    private async Task<IReadOnlyList<RecordEntity>> GetManagedRecordsAsync(Guid zoneId)
    {
        IReadOnlyList<RecordEntity> records = await _recordRepository.ListByZoneIdAsync(zoneId);
        return
        [
            .. records.Where(record => record.ManagedByFeature == RecordManagedByFeatureEnum.Dhcp),
        ];
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_Granted_RegistersARecord()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.15", "workstation");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(result.WasSkipped);
        Assert.Equal("workstation.lease.example.com", result.OwnerName);

        IReadOnlyList<RecordEntity> managed = await GetManagedRecordsAsync(zoneId);
        RecordEntity record = Assert.Single(managed);
        Assert.Equal("workstation.lease.example.com", record.Name);
        Assert.Equal(DnsResourceRecordTypeEnum.A, record.Type);
        ARecordData data = Assert.IsType<ARecordData>(
            Ninlil.Dns.Server.Authoritative.Cache.RecordEntityCodec.DecodeTypedData(record)
        );
        Assert.Equal("192.0.2.15", data.Address.ToString());
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_GrantedTwiceWithNoChange_WritesOnlyOnce()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease2.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.16", "printer");

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);
        DhcpDnsRegistrationResult second = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(second.WasSkipped);

        IReadOnlyList<RecordEntity> managed = await GetManagedRecordsAsync(zoneId);
        Assert.Single(managed);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_GrantedWithNewAddress_OverwritesExistingRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease3.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.17", "laptop");

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        // The client renewed with a different address (after a range change, for example).
        lease.IpAddress = "192.0.2.18";
        _leaseRepository.Update(lease);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        IReadOnlyList<RecordEntity> managed = await GetManagedRecordsAsync(zoneId);
        RecordEntity record = Assert.Single(managed);
        ARecordData data = Assert.IsType<ARecordData>(
            Ninlil.Dns.Server.Authoritative.Cache.RecordEntityCodec.DecodeTypedData(record)
        );
        Assert.Equal("192.0.2.18", data.Address.ToString());
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_GrantedWithNewHostName_RemovesOldRecordAndAddsNew()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease4.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.19", "old-name");

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        lease.HostName = "new-name";
        _leaseRepository.Update(lease);
        await _unitOfWork.SaveChangesAsync();

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        IReadOnlyList<RecordEntity> managed = await GetManagedRecordsAsync(zoneId);
        RecordEntity record = Assert.Single(managed);
        Assert.Equal("new-name.lease4.example.com", record.Name);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_Released_RemovesRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease5.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.21", "phone");

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);
        Assert.Single(await GetManagedRecordsAsync(zoneId));

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Released);

        Assert.False(result.WasSkipped);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_Expired_RemovesRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease6.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.22", "tablet");

        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);
        Assert.Single(await GetManagedRecordsAsync(zoneId));

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Expired);

        Assert.False(result.WasSkipped);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_ScopeWithNoDesignatedZone_IsSkipped()
    {
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", dnsZoneId: null);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.23", "console");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasSkipped);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_IneligibleZoneType_IsSkipped()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease7.example.com", ZoneTypeEnum.Secondary);
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.24", "camera");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasSkipped);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_NoHostName_IsSkipped()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease8.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.25", hostName: null);

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasSkipped);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_HostNameNeedsSanitizing_RegistersCleanLabel()
    {
        Guid zoneId = await SeedForwardZoneAsync("lease9.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.26", "My_Phone!!");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(result.WasSkipped);
        Assert.Equal("my-phone.lease9.example.com", result.OwnerName);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_UnknownLease_IsSkipped()
    {
        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(Guid.NewGuid(), DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasSkipped);
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
