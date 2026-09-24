using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
///     This class builds a real, throwaway SQLite-backed dependency injection host wiring up the
///     real DHCPv4 server engine (<see cref="ServiceCollectionDhcpServerCoreExtension
///     .AddNinlilDhcpServerCore" />) on top of the real Entity Framework Core repository
///     implementations -- the same shape <c>Ninlil.Dhcp.Data.EntityFrameworkCore.Tests</c>'s own
///     <c>DhcpRepositorySearchTestHost</c> establishes, extended with a fixed, deterministic
///     <see cref="DhcpServerOptions" /> configuration (an explicit Server Identifier address, and
///     short offer-hold/decline-quarantine windows) so a test never depends on this sandboxed
///     environment's own real network interfaces or needs to wait out this project's own
///     production-sized default timers.
/// </summary>
public sealed class Dhcpv4EngineTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the fixed Server Identifier address every test in this project
    ///     configures this host with, so a test can assert against it directly rather than reading
    ///     it back through <see cref="Identity.IDhcpServerIdentifierProvider" /> first.
    /// </summary>
    public const string ServerIdentifierAddress = "192.0.2.1";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-engine-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the real dependency injection container this test's own scopes
    ///     are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the real, singleton <see cref="IDhcpv4RequestHandler" /> every
    ///     test in this project drives directly, bypassing the real UDP socket layer (already
    ///     proven separately by <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c> and, for this sub-phase's
    ///     own engine, by <see cref="Dhcpv4UdpRoundTripTests" />) to test the engine's own decision
    ///     logic directly and quickly.
    /// </summary>
    public IDhcpv4RequestHandler Handler =>
        Host.Services.GetRequiredService<IDhcpv4RequestHandler>();

    /// <summary>
    ///     This property contains every <see cref="DhcpLeaseChangedEvent" /> (and any other
    ///     <see cref="IDnsServerEvent" />) published so far --
    ///     <see cref="Allocation.Dhcpv4LeaseAllocationService" />
    ///     and <see cref="Dhcpv4LeaseExpirySweepService" /> both need a real
    ///     <see cref="IDnsServerEventPublisher" /> injected, and this fake, recording
    ///     implementation lets a test assert exactly what was published without needing the real,
    ///     full <c>Ninlil.Dns.Server.Core</c> event dispatcher/channel machinery running at all --
    ///     this project's own engine tests care only about what gets published, never about
    ///     asynchronous delivery to a subscriber (proven separately, end to end, by
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.Tests</c>).
    /// </summary>
    public List<IDnsServerEvent> PublishedEvents { get; } = [];

    /// <summary>
    ///     This method builds and starts the host, applying the entity model directly (via
    ///     <c>EnsureCreatedAsync</c>) rather than the real relational migrations, the same
    ///     shortcut this project's own comparable write-path service tests already take.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DhcpServer:Core:ServerIdentifierAddress"] = ServerIdentifierAddress;
        builder.Configuration["DhcpServer:Core:OfferHoldDuration"] = "00:00:10";
        builder.Configuration["DhcpServer:Core:DeclinedAddressQuarantineDuration"] = "00:00:05";

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        // A fake, recording publisher rather than the real Ninlil.Dns.Server.Core event bus --
        // see PublishedEvents's own doc comment for why.
        builder.Services.AddSingleton<IDnsServerEventPublisher>(
            new RecordingEventPublisher(PublishedEvents)
        );

        builder.Services.AddNinlilDhcpServerCore(builder.Configuration);

        // Registered directly here, mirroring Program.cs's own composition root: the real sweep
        // service is no longer registered inside AddNinlilDhcpServerCore itself, since it must
        // also run when the mutually exclusive relay-agent engine (which never calls that method)
        // is active instead -- see Dhcpv4LeaseExpirySweepService's own doc comment.
        builder.Services.AddHostedService<Dhcpv4LeaseExpirySweepService>();

        // The two protocol-agnostic operator-facing DHCP services (lease force-release, scope
        // statistics) -- registered the same unconditional way Program.cs registers them, for the
        // identical reason the sweep service immediately above is.
        builder.Services.AddNinlilDhcpLeaseAdministration();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method seeds a scope directly through the real repository, in its own fresh scope,
    ///     so a test always exercises the engine against genuinely persisted, freshly-read data
    ///     rather than a tracked in-memory instance the engine's own later scope could see stale
    ///     (Entity Framework Core's identity map would otherwise hand a later scope a stale
    ///     tracked instance).
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
    ///     This method seeds a reservation directly through the real repository, in its own fresh
    ///     scope.
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
    ///     This method reads back the current lease (if any) a client holds in a scope, in its own
    ///     fresh scope, so a test always observes whatever the engine most recently wrote rather
    ///     than a stale tracked instance.
    /// </summary>
    public async Task<LeaseEntity> FindLeaseAsync(string scopeName, string clientIdentifier)
    {
        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        ILeaseRepository repository = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        return await repository.FindByClientIdentifierAsync(scopeName, clientIdentifier);
    }

    /// <summary>
    ///     This method force-releases a lease through the real <see cref="IDhcpLeaseAdministrationService" />,
    ///     in its own fresh scope.
    /// </summary>
    public async Task<LeaseEntity> ForceReleaseLeaseAsync(Guid leaseId)
    {
        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDhcpLeaseAdministrationService service =
            scope.ServiceProvider.GetRequiredService<IDhcpLeaseAdministrationService>();
        return await service.ForceReleaseAsync(leaseId);
    }

    /// <summary>
    ///     This method computes a scope's own current utilization through the real
    ///     <see cref="IDhcpScopeStatisticsService" />, in its own fresh scope.
    /// </summary>
    public async Task<DhcpScopeStatistics> ComputeStatisticsAsync(DhcpScopeEntity scope)
    {
        await using AsyncServiceScope diScope = Host.Services.CreateAsyncScope();
        IDhcpScopeStatisticsService service =
            diScope.ServiceProvider.GetRequiredService<IDhcpScopeStatisticsService>();
        return await service.ComputeAsync(scope);
    }

    /// <summary>
    ///     This method seeds a scope's own operator-configured custom option directly through the
    ///     real repository, in its own fresh scope.
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
    ///     This method seeds a tag-based conditional option delivery rule, plus every option it
    ///     contributes, directly through the real repositories, in its own fresh scope.
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
        IReadOnlySet<byte> serverAddressOptionTags = null
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

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This class is the fake <see cref="IDnsServerEventPublisher" /> backing
    ///     <see cref="PublishedEvents" /> -- see that property's own doc comment for why a fake,
    ///     rather than the real event bus, is registered here.
    /// </summary>
    private sealed class RecordingEventPublisher(List<IDnsServerEvent> events)
        : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) => events.Add(domainEvent);
    }
}
