using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
///     This class builds a dependency injection host for the DHCPv6 engine tests
///     (<see cref="ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore" />) on the
///     Entity Framework Core repositories and a temporary SQLite database file, like
///     <see cref="Dhcpv4EngineTestHost" />: a fixed IPv6 server address (the DUID is still
///     detected from the machine), a short offer hold, optional Rapid Commit, a recording event
///     publisher, and a host that is built but not started.
/// </summary>
public sealed class Dhcpv6EngineTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the IPv6 server address the host is configured with, like
    ///     <see cref="Dhcpv4EngineTestHost.ServerIdentifierAddress" />. It is an RFC 3849
    ///     documentation address, which is neither loopback nor link-local, so
    ///     <see cref="Ninlil.Dhcp.Server.Core.Identity.Dhcpv6ServerAddressProvider" /> accepts it.
    /// </summary>
    public const string ServerAddress = "2001:db8::1";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcpv6-engine-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the host whose services the tests use.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the singleton <see cref="IDhcpv6RequestHandler" />, which the
    ///     tests call directly without a socket.
    /// </summary>
    public IDhcpv6RequestHandler Handler =>
        Host.Services.GetRequiredService<IDhcpv6RequestHandler>();

    /// <summary>
    ///     This property resolves the same singleton as <see cref="Handler" /> as an
    ///     <see cref="IDhcpv6RelayMessageHandler" />, so tests can pass it a RELAY-FORW directly.
    /// </summary>
    public IDhcpv6RelayMessageHandler RelayHandler =>
        Host.Services.GetRequiredService<IDhcpv6RelayMessageHandler>();

    /// <summary>
    ///     This property contains every event published so far, such as
    ///     <see cref="DhcpLeaseChangedEvent" /> (see <see cref="Dhcpv4EngineTestHost.PublishedEvents" />).
    /// </summary>
    public List<IDnsServerEvent> PublishedEvents { get; } = [];

    /// <summary>
    ///     This property records the Warning-or-worse log messages of the host, so a test can check
    ///     how many warnings the option overlay logged for a skipped stored row.
    /// </summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>
    ///     This method builds the host (without starting it) and creates the schema with
    ///     <c>EnsureCreatedAsync</c>, as <see cref="Dhcpv4EngineTestHost" /> does.
    ///     <paramref name="configureServices" /> runs after the engine is registered, so a test can
    ///     replace a service such as <see cref="Ninlil.Dhcp.Server.Core.DnsUpdate.IDhcpDnsUpdatePolicy" />.
    /// </summary>
    public async Task InitializeAsync(
        bool enableRapidCommit = false,
        Action<IServiceCollection> configureServices = null
    )
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DhcpServer:Core:OfferHoldDuration"] = "00:00:10";
        builder.Configuration["DhcpServer:Core:EnableDhcpv6RapidCommit"] =
            enableRapidCommit.ToString();
        builder.Configuration["DhcpServer:Core:ServerIPv6Address"] = ServerAddress;

        builder.Logging.AddProvider(Logs);

        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        builder.Services.AddSingleton<IDnsServerEventPublisher>(
            new RecordingEventPublisher(PublishedEvents)
        );

        builder.Services.AddNinlilDhcpv6ServerCore(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        // Lease force-release and scope statistics, registered as Program.cs does.
        builder.Services.AddNinlilDhcpLeaseAdministration();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method saves an address-assignment scope (<see cref="DhcpScopeKindEnum.AddressAssignment" />)
    ///     through the repository in a separate scope.
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
    ///     This method saves a prefix delegation scope (<see cref="DhcpScopeKindEnum.PrefixDelegation" />)
    ///     through the repository in a separate scope.
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
    ///     This method saves a scope for the two methods above.
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
    ///     This method saves a reservation through the repository in a separate scope; only
    ///     address-assignment scopes use reservations (see
    ///     <see cref="Ninlil.Dhcp.Server.Core.Scope.Dhcpv6ScopeResolver" />).
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
    ///     This method saves a tag rule and its options, like
    ///     <see cref="Dhcpv4EngineTestHost.SeedTagRuleAsync" />, with the DHCPv6 criteria. There is
    ///     no Circuit ID parameter, since
    ///     <see cref="Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService" /> ignores it.
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
    ///     This class is the fake <see cref="IDnsServerEventPublisher" /> that records into
    ///     <see cref="PublishedEvents" />, as in <see cref="Dhcpv4EngineTestHost" />.
    /// </summary>
    private sealed class RecordingEventPublisher(List<IDnsServerEvent> events)
        : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) => events.Add(domainEvent);
    }
}
