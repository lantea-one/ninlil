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
///     This test is this sub-phase's own "live-proven" evidence: a real client-to-server DHCPv4
///     DORA exchange, driven directly against the real <see cref="IDhcpv4RequestHandler" />
///     engine (the UDP socket layer itself is proven separately by
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c>/<c>Ninlil.Dhcp.Server.Core.Tests</c>), against a
///     real, fully-wired combined host -- the real event bus (<c>AddNinlilDnsServerCore</c>), the
///     real lease allocation engine (<c>AddNinlilDhcpServerCore</c>), and the real DHCP-&gt;DNS
///     integration feature under test (<c>AddNinlilDhcpDnsIntegration</c>) -- proving a granted
///     lease's own host name really becomes a real, resolvable A record (resolved through a
///     freshly-loaded <see cref="AuthoritativeZoneCache" />/<see cref="AuthoritativeQueryResolver" />,
///     the same classes that answer a real production query), and that releasing the lease
///     really removes it again. Everything here runs through the real, asynchronous
///     <c>DnsServerEventChangedEvent</c>-&gt;<c>DhcpLeaseChangedEvent</c> pipeline, not a direct
///     method call bypassing it -- <see cref="DhcpDnsRegistrationServiceTests" /> already covers
///     <see cref="DhcpDnsRegistrationService" />'s own business logic directly and exhaustively;
///     this test's only job is proving the wiring between the DHCP engine, the shared event bus,
///     and this feature's own subscriber genuinely works end to end.
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

        // The real event bus -- IDnsServerEventPublisher plus its background dispatcher -- so a
        // lease event this test triggers travels the exact same asynchronous path a real
        // production process uses, not a synchronous stand-in.
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // The minimal slice of Server.Authoritative this test needs (IZoneUpdateService itself),
        // mirroring PtrSyncServiceTests'/CaaRecordReconciliationServiceTests' own established
        // shape rather than pulling in the whole AddNinlilDnsServerAuthoritative extension (cloud
        // provider HTTP clients, zone transfer/notify/DNSSEC machinery) this test has no use for.
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        builder.Services.AddNinlilDhcpServerCore(builder.Configuration);
        builder.Services.AddNinlilDhcpDnsIntegration();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        // Starts every hosted service, most importantly DnsServerEventDispatcher -- without this,
        // a published event would simply sit in its channel forever, never reaching any
        // subscriber at all.
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

        // The grant above only *published* DhcpLeaseChangedEvent -- this polls for the real,
        // asynchronous dispatcher -> subscriber -> DhcpDnsRegistrationService chain to actually
        // finish writing the record, rather than assuming any particular timing.
        RecordEntity aRecord = await WaitForManagedRecordAsync(zoneId, ownerName);

        Assert.NotNull(aRecord);
        Assert.Equal(DnsResourceRecordTypeEnum.A, aRecord.Type);

        // Now prove it is genuinely *resolvable*, through the same real
        // AuthoritativeZoneCache/AuthoritativeQueryResolver classes that answer a real production
        // query -- not merely present as a row in the repository.
        DnsMessage response = await ResolveARecordAsync(ownerName);
        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(new IPAddress(answer.Data), offer.YourIpAddress);

        // Release the lease and prove the same real pipeline removes the record again.
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
    ///     This method resolves a real A query against a freshly-loaded
    ///     <see cref="AuthoritativeZoneCache" />/<see cref="AuthoritativeQueryResolver" /> pair --
    ///     built directly here, rather than through <c>AddNinlilDnsServerAuthoritative</c>, since
    ///     that is the one part of the real production answering path this test actually needs to
    ///     prove against, independent of everything else that extension also wires up.
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
