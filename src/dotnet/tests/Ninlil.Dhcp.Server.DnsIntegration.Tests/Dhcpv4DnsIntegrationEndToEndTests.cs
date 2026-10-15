using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dhcp.Server.DnsIntegration.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This test class checks the DHCP-to-DNS wiring end to end for DHCPv4. A host combines the
///     event bus and dispatcher (<c>AddNinlilDnsServerCore</c>), the DHCPv4 engine
///     (<c>AddNinlilDhcpServerCore</c>), <c>IZoneUpdateService</c> and the feature
///     (<c>AddNinlilDhcpDnsIntegration</c>) over a temporary SQLite file. A DISCOVER-REQUEST
///     exchange is sent to the <see cref="IDhcpv4RequestHandler" /> without a socket, the test
///     waits for the published <see cref="DhcpLeaseChangedEvent" /> to reach the subscriber, and
///     checks that the host name resolves as an A record through a freshly loaded
///     <see cref="AuthoritativeZoneCache" /> and <see cref="AuthoritativeQueryResolver" /> (the
///     classes that answer production queries); a RELEASE then removes it. The service's own
///     rules are covered by <see cref="DhcpDnsRegistrationServiceTests" />.
/// </summary>
public sealed class Dhcpv4DnsIntegrationEndToEndTests : IAsyncLifetime
{
    private const string ServerIdentifierAddress = "192.0.2.1";

    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-dns-e2e-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DhcpServer:Core:ServerIdentifierAddress"] = ServerIdentifierAddress;

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        // The event bus (IDnsServerEventPublisher and its background dispatcher), so lease
        // events take the same asynchronous path as in production.
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Only IZoneUpdateService from Server.Authoritative, as in PtrSyncServiceTests and
        // CaaRecordReconciliationServiceTests, rather than all of AddNinlilDnsServerAuthoritative
        // (cloud providers, zone transfer, NOTIFY, DNSSEC).
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        builder.Services.AddNinlilDhcpServerCore(builder.Configuration);
        builder.Services.AddNinlilDhcpDnsIntegration();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        // Start the hosted services, above all DnsServerEventDispatcher; without it events would
        // stay in the channel and never reach a subscriber.
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task GrantedLease_ProducesARealResolvableARecord_AndReleaseRemovesItAgain()
    {
        const string zoneName = "e2e-lease.example.com";
        byte[] clientHardwareAddress = [0xde, 0xad, 0xbe, 0xef, 0x00, 0x01];

        Guid zoneId = await SeedForwardZoneAsync(zoneName);
        await SeedDhcpScopeAsync("lan", "192.0.2.10", "192.0.2.12", zoneId);

        IDhcpv4RequestHandler handler = _host.Services.GetRequiredService<IDhcpv4RequestHandler>();

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x1000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetHostName("e2e-workstation");

        Dhcpv4Message offer = await handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);
        Assert.True(offer.TryGetServerIdentifier(out IPAddress serverIdentifier));

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x1000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        request.SetHostName("e2e-workstation");

        Dhcpv4Message ack = await handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum ackType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, ackType);

        string ownerName = $"e2e-workstation.{zoneName}";

        // The grant only published DhcpLeaseChangedEvent; poll until the dispatcher, subscriber
        // and DhcpDnsRegistrationService have written the record, instead of assuming a timing.
        RecordEntity aRecord = await WaitForManagedRecordAsync(zoneId, ownerName);

        Assert.NotNull(aRecord);
        Assert.Equal(DnsResourceRecordTypeEnum.A, aRecord.Type);

        // Check it resolves through AuthoritativeZoneCache and AuthoritativeQueryResolver, not
        // just that the row exists.
        DnsMessage response = await ResolveARecordAsync(ownerName);
        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(new IPAddress(answer.Data), offer.YourIpAddress);

        // Release the lease and check the same path removes the record.
        Dhcpv4Message release = NewMessage(clientHardwareAddress, 0x1001);
        release.SetMessageType(Dhcpv4MessageTypeEnum.Release);
        release.ClientIpAddress = offer.YourIpAddress;

        Dhcpv4Message releaseReply = await handler.HandleRequestAsync(
            release,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.Null(releaseReply); // RFC 2131: no reply to a RELEASE.

        await WaitForManagedRecordRemovalAsync(zoneId, ownerName);
    }

    /// <summary>
    ///     This test replays the race the registration must survive: the expiry sweep commits
    ///     Expired, a renewal commits Active again, and the events reach the bus in the order
    ///     Granted then Expired. The lease row ends Active, so its record must still exist
    ///     whichever event is handled last.
    /// </summary>
    [Fact]
    public async Task GrantedThenExpiredEvents_ForALeaseRenewedAfterTheSweep_LeaveTheRecordInPlace()
    {
        const string zoneName = "e2e-race.example.com";

        Guid zoneId = await SeedForwardZoneAsync(zoneName);
        await SeedDhcpScopeAsync("race", "192.0.2.30", "192.0.2.40", zoneId);
        IDnsServerEventPublisher publisher =
            _host.Services.GetRequiredService<IDnsServerEventPublisher>();
        Guid leaseId = Guid.NewGuid();

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            await leases.AddAsync(
                new LeaseEntity
                {
                    Id = leaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "race",
                    ClientIdentifier = "aa:bb:cc:dd:ee:01",
                    IpAddress = "192.0.2.31",
                    HostName = "racer",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                }
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        string ownerName = $"racer.{zoneName}";
        publisher.Publish(
            new DhcpLeaseChangedEvent(
                DateTimeOffset.UtcNow,
                leaseId,
                DhcpLeaseChangeKindEnum.Granted
            )
        );
        await WaitForManagedRecordAsync(zoneId, ownerName);

        // The sweep expires the row, then the client renews it before either event is handled.
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            Assert.True(await leases.TryExpireAsync(leaseId, DateTimeOffset.UtcNow));
        }

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            LeaseEntity lease = await leases.GetByIdAsync(leaseId);
            lease.State = LeaseStateEnum.Active;
            lease.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1);
            leases.Update(lease);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        publisher.Publish(
            new DhcpLeaseChangedEvent(
                DateTimeOffset.UtcNow,
                leaseId,
                DhcpLeaseChangeKindEnum.Granted
            )
        );
        publisher.Publish(
            new DhcpLeaseChangedEvent(
                DateTimeOffset.UtcNow,
                leaseId,
                DhcpLeaseChangeKindEnum.Expired
            )
        );

        // Nothing signals that the bus has drained, so watch the record for a settle period
        // that is long enough for both handlers: it must never disappear.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(50);
            Assert.NotNull(await FindManagedRecordAsync(zoneId, ownerName));
        }
    }

    private async Task<RecordEntity> FindManagedRecordAsync(Guid zoneId, string ownerName)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(zoneId);
        return records.FirstOrDefault(record =>
            record.ManagedByFeature == RecordManagedByFeatureEnum.Dhcp
            && string.Equals(record.Name, ownerName, StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task<Guid> SeedForwardZoneAsync(string name)
    {
        Guid zoneId = Guid.NewGuid();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = name,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(Ninlil.Dns.Common.Wire.Records.SoaRecordData),
                    new Ninlil.Dns.Common.Wire.Records.SoaRecordData(
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
        await unitOfWork.SaveChangesAsync();

        return zoneId;
    }

    private async Task SeedDhcpScopeAsync(
        string name,
        string rangeStartAddress,
        string rangeEndAddress,
        Guid dnsZoneId
    )
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IDhcpScopeRepository scopeRepository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await scopeRepository.AddAsync(
            new DhcpScopeEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Name = name,
                ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                RangeStartAddress = rangeStartAddress,
                RangeEndAddress = rangeEndAddress,
                SubnetPrefixLength = 24,
                DnsZoneId = dnsZoneId,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }

    private async Task<RecordEntity> WaitForManagedRecordAsync(Guid zoneId, string ownerName)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(zoneId);
            RecordEntity match = records.FirstOrDefault(record =>
                record.ManagedByFeature == RecordManagedByFeatureEnum.Dhcp
                && string.Equals(record.Name, ownerName, StringComparison.OrdinalIgnoreCase)
            );

            if (match is not null)
                return match;

            await Task.Delay(50);
        }

        Assert.Fail(
            $"No managed DHCP record named '{ownerName}' appeared in zone {zoneId} within the timeout."
        );
        return null;
    }

    private async Task WaitForManagedRecordRemovalAsync(Guid zoneId, string ownerName)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(zoneId);
            bool stillPresent = records.Any(record =>
                record.ManagedByFeature == RecordManagedByFeatureEnum.Dhcp
                && string.Equals(record.Name, ownerName, StringComparison.OrdinalIgnoreCase)
            );

            if (!stillPresent)
                return;

            await Task.Delay(50);
        }

        Assert.Fail(
            $"The managed DHCP record named '{ownerName}' was never removed within the timeout."
        );
    }

    /// <summary>
    ///     This method answers an A query with a freshly loaded <see cref="AuthoritativeZoneCache" />
    ///     and <see cref="AuthoritativeQueryResolver" />, built here rather than through
    ///     <c>AddNinlilDnsServerAuthoritative</c>, since only that part of the answering path is
    ///     needed.
    /// </summary>
    private async Task<DnsMessage> ResolveARecordAsync(string ownerName)
    {
        AuthoritativeZoneCache zoneCache = new(
            _host.Services.GetRequiredService<IServiceScopeFactory>()
        );
        await zoneCache.StartAsync(CancellationToken.None);

        SignedZoneCache signedZoneCache = new(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignedZoneCache>.Instance
        );

        AuthoritativeQueryResolver resolver = new(
            zoneCache,
            signedZoneCache,
            new StaticOptionsMonitor<AuthoritativeServerOptions>(new AuthoritativeServerOptions())
        );

        DnsQueryContext context = new(
            new DnsMessage
            {
                Id = 1,
                Opcode = DnsOpcodeEnum.Query,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(ownerName),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            },
            DnsTransportProtocolEnum.Udp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(context);
        Assert.True(resolution.IsHandled);

        return resolution.Response;
    }

    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };

    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }
}
