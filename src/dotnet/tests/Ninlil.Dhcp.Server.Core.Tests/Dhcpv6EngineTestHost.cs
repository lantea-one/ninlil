using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv6;
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
///     real DHCPv6 server engine (<see cref="ServiceCollectionDhcpServerCoreExtension
///     .AddNinlilDhcpv6ServerCore" />) on top of the real Entity Framework Core repository
///     implementations -- the DHCPv6 analogue of <see cref="Dhcpv4EngineTestHost" />; see that
///     class's own doc comment for the reasoning this one shares.
/// </summary>
public sealed class Dhcpv6EngineTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the fixed, deterministic server address every test in this
    ///     project configures this host with, so a test can assert against it directly rather than
    ///     depending on this sandboxed environment's own real network interfaces -- the DHCPv6
    ///     analogue of <see cref="Dhcpv4EngineTestHost.ServerIdentifierAddress" />, using an RFC
    ///     3849 documentation-range address (genuinely non-loopback, non-link-local, so
    ///     <see cref="Ninlil.Dhcp.Server.Core.Identity.Dhcpv6ServerAddressProvider" />'s own
    ///     validation accepts it) rather than a real, routable one.
    /// </summary>
    public const string ServerAddress = "2001:db8::1";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcpv6-engine-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the real dependency injection container this test's own scopes
    ///     are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the real, singleton <see cref="IDhcpv6RequestHandler" /> every
    ///     test in this project drives directly, bypassing the real UDP socket layer.
    /// </summary>
    public IDhcpv6RequestHandler Handler =>
        Host.Services.GetRequiredService<IDhcpv6RequestHandler>();

    /// <summary>
    ///     This property resolves the same singleton <see cref="Handler" /> does, through its own
    ///     <see cref="IDhcpv6RelayMessageHandler" /> face instead -- used to drive a RELAY-FORW
    ///     directly, bypassing the real UDP socket layer, the same shortcut <see cref="Handler" />
    ///     already takes for an unrelayed message.
    /// </summary>
    public IDhcpv6RelayMessageHandler RelayHandler =>
        Host.Services.GetRequiredService<IDhcpv6RelayMessageHandler>();

    /// <summary>
    ///     This property contains every <see cref="DhcpLeaseChangedEvent" /> (and any other
    ///     <see cref="IDnsServerEvent" />) published so far -- see
    ///     <see cref="Dhcpv4EngineTestHost.PublishedEvents" />'s own doc comment.
    /// </summary>
    public List<IDnsServerEvent> PublishedEvents { get; } = [];

    /// <summary>
    ///     This method builds and starts the host, applying the entity model directly (via
    ///     <c>EnsureCreatedAsync</c>) rather than the real relational migrations, the same
    ///     shortcut <see cref="Dhcpv4EngineTestHost" /> already takes.
    /// </summary>
    public async Task InitializeAsync(bool enableRapidCommit = false)
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DhcpServer:Core:OfferHoldDuration"] = "00:00:10";
        builder.Configuration["DhcpServer:Core:EnableDhcpv6RapidCommit"] =
            enableRapidCommit.ToString();
        builder.Configuration["DhcpServer:Core:ServerIPv6Address"] = ServerAddress;

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        builder.Services.AddSingleton<IDnsServerEventPublisher>(
            new RecordingEventPublisher(PublishedEvents)
        );

        builder.Services.AddNinlilDhcpv6ServerCore(builder.Configuration);

        // The two protocol-agnostic operator-facing DHCP services (lease force-release, scope
        // statistics) -- registered the same unconditional way Program.cs registers them.
        builder.Services.AddNinlilDhcpLeaseAdministration();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method seeds a stateful address-assignment scope (<see cref="DhcpScopeKindEnum.AddressAssignment" />)
    ///     directly through the real repository, in its own fresh scope.
    /// </summary>
    public async Task<DhcpScopeEntity> SeedAddressScopeAsync(
        string name,
        string rangeStartAddress,
        string rangeEndAddress,
        int subnetPrefixLength = 64,
        TimeSpan? defaultLeaseDuration = null,
        TimeSpan? maxLeaseDuration = null,
        IReadOnlyList<string> dnsServerAddresses = null
    ) =>
        await SeedScopeAsync(
            name,
            DhcpScopeKindEnum.AddressAssignment,
            rangeStartAddress,
            rangeEndAddress,
            subnetPrefixLength,
            delegatedPrefixLength: null,
            defaultLeaseDuration,
            maxLeaseDuration,
            dnsServerAddresses
        );

    /// <summary>
    ///     This method seeds a Prefix Delegation scope (<see cref="DhcpScopeKindEnum.PrefixDelegation" />)
    ///     directly through the real repository, in its own fresh scope.
    /// </summary>
    public async Task<DhcpScopeEntity> SeedPrefixDelegationScopeAsync(
        string name,
        string rangeStartAddress,
        string rangeEndAddress,
        int delegatedPrefixLength,
        int subnetPrefixLength = 48,
        TimeSpan? defaultLeaseDuration = null,
        TimeSpan? maxLeaseDuration = null
    ) =>
        await SeedScopeAsync(
            name,
            DhcpScopeKindEnum.PrefixDelegation,
            rangeStartAddress,
            rangeEndAddress,
            subnetPrefixLength,
            delegatedPrefixLength,
            defaultLeaseDuration,
            maxLeaseDuration,
            dnsServerAddresses: null
        );

    /// <summary>
    ///     This method performs the actual scope-seeding both public methods above share.
    /// </summary>
    private async Task<DhcpScopeEntity> SeedScopeAsync(
        string name,
        DhcpScopeKindEnum kind,
        string rangeStartAddress,
        string rangeEndAddress,
        int subnetPrefixLength,
        int? delegatedPrefixLength,
        TimeSpan? defaultLeaseDuration,
        TimeSpan? maxLeaseDuration,
        IReadOnlyList<string> dnsServerAddresses
    )
    {
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv6,
            Kind = kind,
            RangeStartAddress = rangeStartAddress,
            RangeEndAddress = rangeEndAddress,
            SubnetPrefixLength = subnetPrefixLength,
            DelegatedPrefixLength = delegatedPrefixLength,
            DefaultLeaseDuration = defaultLeaseDuration ?? TimeSpan.FromHours(1),
            MaxLeaseDuration = maxLeaseDuration ?? TimeSpan.FromDays(1),
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
    ///     This method seeds a static reservation directly through the real repository, in its own
    ///     fresh scope -- address-assignment scopes only, see
    ///     <see cref="Ninlil.Dhcp.Server.Core.Scope.Dhcpv6ScopeResolver" />'s own doc comment.
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
    ///     This method seeds a tag-based conditional option delivery rule, plus every option it
    ///     contributes, directly through the real repositories, in its own fresh scope -- the
    ///     DHCPv6 analogue of <see cref="Dhcpv4EngineTestHost.SeedTagRuleAsync" />, taking this
    ///     protocol's own match-criteria subset (<see cref="Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService" />
    ///     never consults <c>relayAgentCircuitIdHex</c> at all, so this method has no parameter for
    ///     it).
    /// </summary>
    public async Task<DhcpTagRuleEntity> SeedTagRuleAsync(
        string name,
        IReadOnlyDictionary<byte, string> options,
        Guid? scopeId = null,
        int priority = 0,
        string vendorClassIdentifierMatch = null,
        Dhcpv4TagMatchModeEnum vendorClassIdentifierMatchMode = Dhcpv4TagMatchModeEnum.Exact,
        uint? vendorClassEnterpriseNumber = null,
        string userClassMatch = null,
        Dhcpv4TagMatchModeEnum userClassMatchMode = Dhcpv4TagMatchModeEnum.Exact,
        string relayInterfaceIdHex = null,
        string relayRemoteIdHex = null,
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
            VendorClassEnterpriseNumber = vendorClassEnterpriseNumber,
            UserClassMatch = userClassMatch,
            UserClassMatchMode = userClassMatchMode,
            RelayInterfaceIdHex = relayInterfaceIdHex,
            RelayAgentRemoteIdHex = relayRemoteIdHex,
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
    ///     <see cref="PublishedEvents" /> -- see <see cref="Dhcpv4EngineTestHost.RecordingEventPublisher" />'s
    ///     own doc comment for why a fake, rather than the real event bus, is registered here.
    /// </summary>
    private sealed class RecordingEventPublisher(List<IDnsServerEvent> events)
        : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) => events.Add(domainEvent);
    }
}
