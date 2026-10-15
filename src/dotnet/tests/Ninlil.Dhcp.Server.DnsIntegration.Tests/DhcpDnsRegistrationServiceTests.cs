using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private DhcpDnsRegistrationService CreateService(
        ILogger<DhcpDnsRegistrationService> logger = null
    )
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new DhcpDnsRegistrationService(
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>(),
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            logger ?? NullLogger<DhcpDnsRegistrationService>.Instance
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
        LeaseStateEnum state = LeaseStateEnum.Active,
        string clientIdentifier = "aa:bb:cc:dd:ee:ff",
        DateTimeOffset? expiresAtUtc = null,
        InternetProtocolVersionEnum protocol = InternetProtocolVersionEnum.IPv4
    )
    {
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = protocol,
            ScopeName = scopeName,
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
            HostName = hostName,
            State = state,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
            LeaseExpiresAtUtc = expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(1),
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

    /// <summary>
    ///     This method stores one record at a name: an operator's (static) record, or one DHCP
    ///     manages, depending on <paramref name="managedBy" />.
    /// </summary>
    private async Task SeedRecordAsync(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum type,
        string dataJson,
        RecordManagedByFeatureEnum? managedBy
    )
    {
        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = name,
                Type = type,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = dataJson,
                ManagedByFeature = managedBy,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();
    }

    private static string AData(string address) =>
        Ninlil.Common.Serializer.JsonSerializer.Serialize(
            typeof(ARecordData),
            new ARecordData(System.Net.IPAddress.Parse(address))
        );

    private static string AaaaData(string address) =>
        Ninlil.Common.Serializer.JsonSerializer.Serialize(
            typeof(AaaaRecordData),
            new AaaaRecordData(System.Net.IPAddress.Parse(address))
        );

    private static string TxtData(string text) =>
        Ninlil.Common.Serializer.JsonSerializer.Serialize(
            typeof(TxtRecordData),
            new TxtRecordData([text])
        );

    private static string CnameData(string target) =>
        Ninlil.Common.Serializer.JsonSerializer.Serialize(
            typeof(CnameRecordData),
            new CnameRecordData(DnsName.Parse(target))
        );

    [Fact]
    public async Task HandleLeaseChangeAsync_StaticARecordAtTheName_IsRefusedAndLogged()
    {
        Guid zoneId = await SeedForwardZoneAsync("c1.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        await SeedRecordAsync(
            zoneId,
            "nas.c1.example.com",
            DnsResourceRecordTypeEnum.A,
            AData("192.0.2.200"),
            null
        );
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.30", "nas");
        CapturingLogger logger = new();

        DhcpDnsRegistrationResult result = await CreateService(logger)
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        Assert.True(result.WasSkipped);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
        IReadOnlyList<RecordEntity> all = await _recordRepository.ListByZoneIdAsync(zoneId);
        RecordEntity staticRecord = Assert.Single(
            all,
            record => record.Name == "nas.c1.example.com"
        );
        Assert.Equal(AData("192.0.2.200"), staticRecord.DataJson);
        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("aa:bb:cc:dd:ee:ff", warning);
        Assert.Contains("nas.c1.example.com", warning);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_StaticAaaaRecordAtTheName_IsRefused()
    {
        Guid zoneId = await SeedForwardZoneAsync("c1b.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan6", zoneId);
        await SeedRecordAsync(
            zoneId,
            "nas.c1b.example.com",
            DnsResourceRecordTypeEnum.Aaaa,
            AaaaData("2001:db8::200"),
            null
        );
        LeaseEntity lease = await SeedLeaseAsync(
            scope.Name,
            "2001:db8::30",
            "nas",
            protocol: InternetProtocolVersionEnum.IPv6
        );

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_StaticCnameAtTheName_IsRefused()
    {
        Guid zoneId = await SeedForwardZoneAsync("c2.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        await SeedRecordAsync(
            zoneId,
            "web.c2.example.com",
            DnsResourceRecordTypeEnum.Cname,
            CnameData("www.c2.example.com"),
            null
        );
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.31", "web");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_StaticTxtAtTheName_IsRefused()
    {
        Guid zoneId = await SeedForwardZoneAsync("c3.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        await SeedRecordAsync(
            zoneId,
            "note.c3.example.com",
            DnsResourceRecordTypeEnum.Txt,
            TxtData("reserved"),
            null
        );
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.32", "note");
        CapturingLogger logger = new();

        DhcpDnsRegistrationResult result = await CreateService(logger)
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
        Assert.Contains("Txt", Assert.Single(logger.Warnings));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_NameOfAnotherClientsActiveLease_IsRefusedAndTheirRecordKept()
    {
        Guid zoneId = await SeedForwardZoneAsync("c4.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity first = await SeedLeaseAsync(
            scope.Name,
            "192.0.2.40",
            "printer",
            clientIdentifier: "11:11:11:11:11:11"
        );
        await CreateService().HandleLeaseChangeAsync(first.Id, DhcpLeaseChangeKindEnum.Granted);
        LeaseEntity second = await SeedLeaseAsync(
            scope.Name,
            "192.0.2.41",
            "printer",
            clientIdentifier: "22:22:22:22:22:22"
        );
        CapturingLogger logger = new();

        DhcpDnsRegistrationResult result = await CreateService(logger)
            .HandleLeaseChangeAsync(second.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        RecordEntity kept = Assert.Single(await GetManagedRecordsAsync(zoneId));
        Assert.Equal(AData("192.0.2.40"), kept.DataJson);
        Assert.Contains("11:11:11:11:11:11", Assert.Single(logger.Warnings));
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_NameOfAnotherClientWhoseLeaseExpired_IsReplaced()
    {
        Guid zoneId = await SeedForwardZoneAsync("c5.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity first = await SeedLeaseAsync(
            scope.Name,
            "192.0.2.50",
            "printer",
            clientIdentifier: "11:11:11:11:11:11"
        );
        await CreateService().HandleLeaseChangeAsync(first.Id, DhcpLeaseChangeKindEnum.Granted);
        first.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        _leaseRepository.Update(first);
        await _unitOfWork.SaveChangesAsync();
        LeaseEntity second = await SeedLeaseAsync(
            scope.Name,
            "192.0.2.51",
            "printer",
            clientIdentifier: "22:22:22:22:22:22"
        );

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(second.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(result.WasRefused);
        RecordEntity record = Assert.Single(await GetManagedRecordsAsync(zoneId));
        Assert.Equal(AData("192.0.2.51"), record.DataJson);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_NameOfARecordWithNoLiveLease_IsReplaced()
    {
        Guid zoneId = await SeedForwardZoneAsync("c6.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        await SeedRecordAsync(
            zoneId,
            "camera.c6.example.com",
            DnsResourceRecordTypeEnum.A,
            AData("192.0.2.60"),
            RecordManagedByFeatureEnum.Dhcp
        );
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.61", "camera");

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(result.WasRefused);
        RecordEntity record = Assert.Single(await GetManagedRecordsAsync(zoneId));
        Assert.Equal(AData("192.0.2.61"), record.DataJson);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_OneClientMovingToANewAddress_KeepsItsName()
    {
        Guid zoneId = await SeedForwardZoneAsync("c7.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.10", "laptop");
        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        lease.IpAddress = "192.0.2.11";
        _leaseRepository.Update(lease);
        await _unitOfWork.SaveChangesAsync();

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.False(result.WasRefused);
        RecordEntity record = Assert.Single(await GetManagedRecordsAsync(zoneId));
        Assert.Equal(AData("192.0.2.11"), record.DataJson);
    }

    [Fact]
    public async Task HandleLeaseChangeAsync_RefusedNameForAClientRegisteredUnderAnotherName_RemovesTheOldRecord()
    {
        Guid zoneId = await SeedForwardZoneAsync("c8.example.com");
        DhcpScopeEntity scope = await SeedDhcpScopeAsync("lan", zoneId);
        await SeedRecordAsync(
            zoneId,
            "nas.c8.example.com",
            DnsResourceRecordTypeEnum.A,
            AData("192.0.2.201"),
            null
        );
        LeaseEntity lease = await SeedLeaseAsync(scope.Name, "192.0.2.70", "old");
        await CreateService().HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);
        Assert.Single(await GetManagedRecordsAsync(zoneId));

        lease.HostName = "nas";
        _leaseRepository.Update(lease);
        await _unitOfWork.SaveChangesAsync();

        DhcpDnsRegistrationResult result = await CreateService()
            .HandleLeaseChangeAsync(lease.Id, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(result.WasRefused);
        Assert.Empty(await GetManagedRecordsAsync(zoneId));
    }

    /// <summary>
    ///     This class is a logger that keeps the formatted text of every Warning, so a test can
    ///     check what the operator is told.
    /// </summary>
    private sealed class CapturingLogger : ILogger<DhcpDnsRegistrationService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
