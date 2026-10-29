using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Core.Administration;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dhcp.Server.Core.Statistics;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This class builds a dependency injection host for the DHCPv4 engine tests: the engine
///     (<see cref="ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpServerCore" />), the lease
///     administration services and the expiry sweep, on the Entity Framework Core repositories and
///     a temporary SQLite database file, like <c>DhcpRepositorySearchTestHost</c> in
///     <c>Ninlil.Dhcp.Data.EntityFrameworkCore.Tests</c>. <see cref="DhcpServerOptions" /> get a
///     fixed Server Identifier and short offer-hold and quarantine times, so tests do not depend
///     on the machine's interfaces or wait for the default timers. Events go to a recording fake
///     publisher. The host is built but not started, so hosted services do not run.
/// </summary>
public sealed class Dhcpv4EngineTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the Server Identifier address the host is configured with, so
    ///     tests can assert against it without asking
    ///     <see cref="Identity.IDhcpServerIdentifierProvider" />.
    /// </summary>
    public const string ServerIdentifierAddress = "192.0.2.1";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-engine-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the host whose services the tests use.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the singleton <see cref="IDhcpv4RequestHandler" />, which the
    ///     tests call directly without a socket. The socket path is covered by
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c> and <see cref="Dhcpv4UdpRoundTripTests" />.
    /// </summary>
    public IDhcpv4RequestHandler Handler =>
        Host.Services.GetRequiredService<IDhcpv4RequestHandler>();

    /// <summary>
    ///     This property contains every event published so far, such as
    ///     <see cref="DhcpLeaseChangedEvent" />. A recording fake replaces the
    ///     <c>Ninlil.Dns.Server.Core</c> event bus, since these tests check what is published, not
    ///     delivery to subscribers (which <c>Ninlil.Dhcp.Server.DnsIntegration.Tests</c> covers).
    /// </summary>
    public List<IDnsServerEvent> PublishedEvents { get; } = [];

    /// <summary>
    ///     This property records the Warning-or-worse log messages of the host, so a test can check
    ///     how many warnings the option overlay logged for a skipped stored row.
    /// </summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>
    ///     This method builds the host (without starting it) and creates the schema with
    ///     <c>EnsureCreatedAsync</c> instead of running the migrations.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DhcpServer:Core:ServerIdentifierAddress"] = ServerIdentifierAddress;
        builder.Configuration["DhcpServer:Core:OfferHoldDuration"] = "00:00:10";
        builder.Configuration["DhcpServer:Core:DeclinedAddressQuarantineDuration"] = "00:00:05";

        builder.Logging.AddProvider(Logs);

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        // A recording fake instead of the event bus (see PublishedEvents).
        builder.Services.AddSingleton<IDnsServerEventPublisher>(
            new RecordingEventPublisher(PublishedEvents)
        );

        builder.Services.AddNinlilDhcpServerCore(builder.Configuration);

        // Registered here, as in Program.cs, rather than by AddNinlilDhcpServerCore, since it also
        // runs when the relay agent replaces the engine (see Dhcpv4LeaseExpirySweepService).
        builder.Services.AddHostedService<Dhcpv4LeaseExpirySweepService>();

        // Lease force-release and scope statistics, registered as Program.cs does.
        builder.Services.AddNinlilDhcpLeaseAdministration();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method saves a scope through the repository in a separate dependency injection
    ///     scope, so the engine later reads it from the database rather than a tracked instance.
    /// </summary>
    public async Task<DhcpScopeEntity> SeedScopeAsync(
        string name,
        string rangeStartAddress,
        string rangeEndAddress,
        int subnetPrefixLength = 24,
        TimeSpan? defaultLeaseDuration = null,
        TimeSpan? maxLeaseDuration = null,
        IReadOnlyList<string> gatewayAddresses = null,
        IReadOnlyList<string> dnsServerAddresses = null,
        string bootFileName = null,
        string nextServerAddress = null
    )
    {
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = rangeStartAddress,
            RangeEndAddress = rangeEndAddress,
            SubnetPrefixLength = subnetPrefixLength,
            DefaultLeaseDuration = defaultLeaseDuration ?? TimeSpan.FromHours(1),
            MaxLeaseDuration = maxLeaseDuration ?? TimeSpan.FromDays(1),
            BootFileName = bootFileName,
            NextServerAddress = nextServerAddress,
            GatewayAddresses = gatewayAddresses?.ToList() ?? [],
            DnsServerAddresses = dnsServerAddresses?.ToList() ?? [],
        };

        await using AsyncServiceScope scope1 = Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope1.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        await repository.AddAsync(scope);
        await scope1.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return scope;
    }

    /// <summary>
    ///     This method saves a reservation through the repository in a separate scope.
    /// </summary>
    public async Task<DhcpReservationEntity> SeedReservationAsync(
        Guid scopeId,
        string clientIdentifier,
        string ipAddress
    )
    {
        DhcpReservationEntity reservation = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ScopeId = scopeId,
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
        };

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDhcpReservationRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
        await repository.AddAsync(reservation);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return reservation;
    }

    /// <summary>
    ///     This method reads a client's lease in a scope, if any, in a separate scope, so the test
    ///     sees what the engine last saved.
    /// </summary>
    public async Task<LeaseEntity> FindLeaseAsync(string scopeName, string clientIdentifier)
    {
        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        ILeaseRepository repository = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        return await repository.FindByClientIdentifierAsync(scopeName, clientIdentifier);
    }

    /// <summary>
    ///     This method force-releases a lease through <see cref="IDhcpLeaseAdministrationService" />
    ///     in a separate scope.
    /// </summary>
    public async Task<LeaseEntity> ForceReleaseLeaseAsync(Guid leaseId)
    {
        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDhcpLeaseAdministrationService service =
            scope.ServiceProvider.GetRequiredService<IDhcpLeaseAdministrationService>();
        return await service.ForceReleaseAsync(leaseId);
    }

    /// <summary>
    ///     This method computes a scope's utilization through
    ///     <see cref="IDhcpScopeStatisticsService" /> in a separate scope.
    /// </summary>
    public async Task<DhcpScopeStatistics> ComputeStatisticsAsync(DhcpScopeEntity scope)
    {
        await using AsyncServiceScope diScope = Host.Services.CreateAsyncScope();
        IDhcpScopeStatisticsService service =
            diScope.ServiceProvider.GetRequiredService<IDhcpScopeStatisticsService>();
        return await service.ComputeAsync(scope);
    }

    /// <summary>
    ///     This method saves a custom option for a scope through the repository in a separate
    ///     scope.
    /// </summary>
    public async Task<DhcpScopeCustomOptionEntity> SeedCustomOptionAsync(
        Guid scopeId,
        byte optionTag,
        string valueHex,
        bool isDisabled = false,
        DhcpOptionValueSourceEnum valueSource = DhcpOptionValueSourceEnum.Custom
    )
    {
        DhcpScopeCustomOptionEntity customOption = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ScopeId = scopeId,
            OptionTag = optionTag,
            ValueHex = valueHex,
            IsDisabled = isDisabled,
            ValueSource = valueSource,
        };

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDhcpScopeCustomOptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeCustomOptionRepository>();
        await repository.AddAsync(customOption);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return customOption;
    }

    /// <summary>
    ///     This method saves a tag rule and its options through the repositories in separate
    ///     scopes. <c>relayInterfaceIdHex</c> and <c>vendorClassEnterpriseNumber</c> are DHCPv6-only
    ///     criteria, seeded to prove that a DHCPv4 rule carrying them never matches. Tags listed
    ///     in <c>serverAddressOptionTags</c> get the
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> value source.
    /// </summary>
    public async Task<DhcpTagRuleEntity> SeedTagRuleAsync(
        string name,
        IReadOnlyDictionary<byte, string> options,
        Guid? scopeId = null,
        int priority = 0,
        string vendorClassIdentifierMatch = null,
        Dhcpv4TagMatchModeEnum vendorClassIdentifierMatchMode = Dhcpv4TagMatchModeEnum.Exact,
        string userClassMatch = null,
        Dhcpv4TagMatchModeEnum userClassMatchMode = Dhcpv4TagMatchModeEnum.Exact,
        string relayAgentCircuitIdHex = null,
        string relayAgentRemoteIdHex = null,
        bool isDisabled = false,
        IReadOnlySet<byte> serverAddressOptionTags = null,
        string relayInterfaceIdHex = null,
        uint? vendorClassEnterpriseNumber = null
    )
    {
        DhcpTagRuleEntity rule = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ScopeId = scopeId,
            Priority = priority,
            VendorClassIdentifierMatch = vendorClassIdentifierMatch,
            VendorClassIdentifierMatchMode = vendorClassIdentifierMatchMode,
            UserClassMatch = userClassMatch,
            UserClassMatchMode = userClassMatchMode,
            RelayAgentCircuitIdHex = relayAgentCircuitIdHex,
            RelayAgentRemoteIdHex = relayAgentRemoteIdHex,
            RelayInterfaceIdHex = relayInterfaceIdHex,
            VendorClassEnterpriseNumber = vendorClassEnterpriseNumber,
            IsDisabled = isDisabled,
        };

        await using (AsyncServiceScope scope = Host.Services.CreateAsyncScope())
        {
            IDhcpTagRuleRepository repository =
                scope.ServiceProvider.GetRequiredService<IDhcpTagRuleRepository>();
            await repository.AddAsync(rule);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = Host.Services.CreateAsyncScope())
        {
            IDhcpTagRuleOptionRepository optionRepository =
                scope.ServiceProvider.GetRequiredService<IDhcpTagRuleOptionRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach ((byte optionTag, string valueHex) in options)
            {
                await optionRepository.AddAsync(
                    new DhcpTagRuleOptionEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        TagRuleId = rule.Id,
                        OptionTag = optionTag,
                        ValueHex = valueHex,
                        ValueSource =
                            serverAddressOptionTags?.Contains(optionTag) == true
                                ? DhcpOptionValueSourceEnum.ServerAddress
                                : DhcpOptionValueSourceEnum.Custom,
                    }
                );
            }

            await unitOfWork.SaveChangesAsync();
        }

        return rule;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Host?.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This class is the fake <see cref="IDnsServerEventPublisher" /> that records into
    ///     <see cref="PublishedEvents" />.
    /// </summary>
    private sealed class RecordingEventPublisher(List<IDnsServerEvent> events)
        : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) => events.Add(domainEvent);
    }
}
