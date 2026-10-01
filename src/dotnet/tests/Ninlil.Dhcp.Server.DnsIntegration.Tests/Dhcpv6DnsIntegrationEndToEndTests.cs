using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv6;
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
///     This test class is the DHCPv6 counterpart of <see cref="Dhcpv4DnsIntegrationEndToEndTests" />,
///     on the same kind of combined host with the DHCPv6 engine. A SOLICIT, a REQUEST with an RFC
///     4704 Client FQDN option, and a RELEASE are sent to the <see cref="IDhcpv6RequestHandler" />;
///     the test checks that the name resolves as an AAAA record
///     (<see cref="DhcpDnsRegistrationService" /> chooses the type from
///     <see cref="LeaseEntity.ProtocolVersion" />), that the RELEASE removes it, and that the
///     reply's Client FQDN option says the server performs the update (S=1) and, since the client
///     sent S=0, sets the override bit (O=1) as RFC 4704 Section 4.1 requires.
/// </summary>
public sealed class Dhcpv6DnsIntegrationEndToEndTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.IPv6Loopback, 546);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcpv6-dns-e2e-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        // The event bus, as in Dhcpv4DnsIntegrationEndToEndTests, so lease events take the
        // production path.
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Only IZoneUpdateService from Server.Authoritative, as in
        // Dhcpv4DnsIntegrationEndToEndTests.
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        builder.Services.AddNinlilDhcpv6ServerCore(builder.Configuration);
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
    public async Task GrantedLease_WithClientFqdn_ProducesARealResolvableAaaaRecord_AndReleaseRemovesItAgain()
    {
        const string zoneName = "e2e-lease-v6.example.com";
        byte[] duid = [0x00, 0x03, 0x00, 0x01, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

        Guid zoneId = await SeedForwardZoneAsync(zoneName);
        await SeedDhcpv6ScopeAsync("lan6", "2001:db8:1::10", "2001:db8:1::20", zoneId);

        IDhcpv6RequestHandler handler = _host.Services.GetRequiredService<IDhcpv6RequestHandler>();

        // SOLICIT without Rapid Commit and without a Client FQDN option, which clients usually
        // send only with the REQUEST.
        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x1000,
        };
        solicit.SetClientIdentifier(duid);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );

        Dhcpv6Message advertise = await handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(advertise);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Assert.True(advertise.TryGetServerIdentifier(out byte[] serverDuid));
        Assert.True(
            advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> advertised
            )
        );
        IPAddress offeredAddress = Assert.Single(advertised).Addresses[0].Address;

        // REQUEST: names the offered address and sends a partial RFC 4704 Client FQDN with S=0
        // (the client says it will update AAAA itself). The server takes the update anyway, so
        // the reply must carry S=1 and the override bit O=1 (RFC 4704 Section 4.1).
        Dhcpv6Message request = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Request,
            TransactionId = 0x1001,
        };
        request.SetClientIdentifier(duid);
        request.SetServerIdentifier(serverDuid);
        request.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses
            {
                IdentityAssociationId = 1,
                Addresses = { new Dhcpv6IdentityAssociationAddress { Address = offeredAddress } },
            }
        );
        request.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: false,
            serverOverrodeClientPreference: false,
            serverPerformsAaaaUpdate: false,
            domainName: "e2e-workstation-v6",
            isFullyQualified: false
        );

        Dhcpv6Message reply = await handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(
            reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> committed
            )
        );
        Assert.Equal(offeredAddress, Assert.Single(committed).Addresses[0].Address);

        // The reply must say the server performs the AAAA update (S=1), that this overrides the
        // client (O=1), and copy the client's domain name and qualification (RFC 4704 Section 6
        // allows copying).
        Assert.True(
            reply.TryGetClientFqdn(
                out bool replyN,
                out bool replyO,
                out bool replyS,
                out string replyDomainName,
                out bool replyIsFullyQualified
            )
        );
        Assert.False(replyN);
        Assert.True(replyO);
        Assert.True(replyS);
        Assert.Equal("e2e-workstation-v6", replyDomainName);
        Assert.False(replyIsFullyQualified);

        string ownerName = $"e2e-workstation-v6.{zoneName}";

        // The grant only published DhcpLeaseChangedEvent; poll until the record is written.
        RecordEntity aaaaRecord = await WaitForManagedRecordAsync(zoneId, ownerName);

        Assert.NotNull(aaaaRecord);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, aaaaRecord.Type);

        // Check it resolves through AuthoritativeZoneCache and AuthoritativeQueryResolver.
        DnsMessage response = await ResolveAaaaRecordAsync(ownerName);
        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(offeredAddress, new IPAddress(answer.Data));

        // Release the lease and check the same path removes the record.
        Dhcpv6Message release = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Release,
            TransactionId = 0x1002,
        };
        release.SetClientIdentifier(duid);
        release.SetServerIdentifier(serverDuid);
        release.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses
            {
                IdentityAssociationId = 1,
                Addresses = { new Dhcpv6IdentityAssociationAddress { Address = offeredAddress } },
            }
        );

        Dhcpv6Message releaseReply = await handler.HandleRequestAsync(
            release,
            RemoteEndPoint,
            CancellationToken.None
        );
        // Unlike DHCPv4, a DHCPv6 server replies to a RELEASE (RFC 8415).
        Assert.NotNull(releaseReply);
        Assert.True(
            releaseReply.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> releasedAssociations
            )
        );
        Assert.True(
            Assert
                .Single(releasedAssociations)
                .Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum statusCode, out _)
        );
        Assert.Equal(Dhcpv6StatusCodeEnum.Success, statusCode);

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

    private async Task SeedDhcpv6ScopeAsync(
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
                ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                Kind = DhcpScopeKindEnum.AddressAssignment,
                RangeStartAddress = rangeStartAddress,
                RangeEndAddress = rangeEndAddress,
                SubnetPrefixLength = 64,
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
    ///     This method answers an AAAA query with a freshly loaded cache and resolver, as
    ///     <see cref="Dhcpv4DnsIntegrationEndToEndTests.ResolveARecordAsync" /> does for A.
    /// </summary>
    private async Task<DnsMessage> ResolveAaaaRecordAsync(string ownerName)
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
                        DnsResourceRecordTypeEnum.Aaaa,
                        DnsClassEnum.Internet
                    ),
                },
            },
            DnsTransportProtocolEnum.Udp,
            IPAddress.IPv6Loopback,
            DateTimeOffset.UtcNow
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(context);
        Assert.True(resolution.IsHandled);

        return resolution.Response;
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }
}
