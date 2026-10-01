using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpClientRegistryAutoPopulationService" />: new devices,
///     renewals that leave an operator's rename alone, linking a DHCPv6 identifier to a DHCPv4
///     device, ambiguous links, and presence sightings. Like
///     <see cref="DhcpDnsRegistrationServiceTests" />, it calls the service directly on the
///     repositories over a temporary SQLite file, without the event bus (whose wiring
///     <see cref="Dhcpv4DnsIntegrationEndToEndTests" /> covers), and uses a recording presence
///     recorder.
/// </summary>
public sealed class DhcpClientRegistryAutoPopulationServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-client-registry-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private ILeaseRepository _leaseRepository;
    private IDnsClientRepository _clientRepository;
    private IDnsClientAddressRepository _clientAddressRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _leaseRepository = _host.Services.GetRequiredService<ILeaseRepository>();
        _clientRepository = _host.Services.GetRequiredService<IDnsClientRepository>();
        _clientAddressRepository = _host.Services.GetRequiredService<IDnsClientAddressRepository>();
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
    ///     This method builds a <see cref="DhcpClientRegistryAutoPopulationService" /> on the
    ///     repositories of a new dependency injection scope for each call, as
    ///     <c>DhcpDnsRegistrationServiceTests.CreateService</c> does, so no tracked entity from an
    ///     earlier step is reused.
    /// </summary>
    private DhcpClientRegistryAutoPopulationService CreateService(
        IClientPresenceRecorder presenceRecorder = null
    )
    {
        IServiceScope scope = _host.Services.CreateScope();
        return new DhcpClientRegistryAutoPopulationService(
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>(),
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>(),
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<DhcpClientRegistryAutoPopulationService>.Instance,
            presenceRecorder
        );
    }

    private int _nextLeaseAddressOctet = 50;

    private async Task<Guid> SeedGrantedLeaseAsync(
        string clientIdentifier,
        string hostName,
        InternetProtocolVersionEnum protocolVersion = InternetProtocolVersionEnum.IPv4
    )
    {
        Guid leaseId = Guid.NewGuid();

        // Each seeded lease gets its own address, because of the (ScopeName, IpAddress) unique
        // index.
        string ipAddress = $"192.0.2.{_nextLeaseAddressOctet++}";

        await _leaseRepository.AddAsync(
            new LeaseEntity
            {
                Id = leaseId,
                ProtocolVersion = protocolVersion,
                ScopeName = "lan",
                ClientIdentifier = clientIdentifier,
                IpAddress = ipAddress,
                HostName = hostName,
                State = LeaseStateEnum.Active,
                LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        return leaseId;
    }

    [Fact]
    public async Task GrantedLease_ForAPreviouslyUnseenIdentifier_CreatesAKnownClientRow()
    {
        const string clientIdentifier = "AA:BB:CC:DD:EE:01";
        Guid leaseId = await SeedGrantedLeaseAsync(clientIdentifier, "workstation-1");

        bool created = await CreateService()
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(created);

        DnsClientEntity client = await _clientRepository.GetByClientIdentifierAsync(
            clientIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        Assert.NotNull(client);
        Assert.Equal("workstation-1", client.DisplayName);
        Assert.Equal(DnsClientOriginEnum.Dhcp, client.Origin);

        // The device's single identifier row was created with it, holding the lease's
        // identifier.
        IReadOnlyList<DnsClientAddressEntity> addresses =
            await _clientAddressRepository.ListByClientIdAsync(client.Id);
        DnsClientAddressEntity address = Assert.Single(addresses);
        Assert.Equal(clientIdentifier, address.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, address.ProtocolVersion);
    }

    [Fact]
    public async Task GrantedLease_WithNoHostName_CreatesARowWithANullDisplayName()
    {
        const string clientIdentifier = "AA:BB:CC:DD:EE:02";
        Guid leaseId = await SeedGrantedLeaseAsync(clientIdentifier, hostName: null);

        bool created = await CreateService()
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);

        Assert.True(created);

        DnsClientEntity client = await _clientRepository.GetByClientIdentifierAsync(
            clientIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        Assert.NotNull(client);
        Assert.Null(client.DisplayName);
    }

    [Fact]
    public async Task GrantedLease_ForAnAlreadyKnownIdentifier_NeverOverwritesAnOperatorEditedDisplayName()
    {
        const string clientIdentifier = "AA:BB:CC:DD:EE:03";
        Guid firstLeaseId = await SeedGrantedLeaseAsync(clientIdentifier, "workstation-3");

        DhcpClientRegistryAutoPopulationService service = CreateService();
        bool firstCreated = await service.HandleLeaseChangeAsync(
            firstLeaseId,
            DhcpLeaseChangeKindEnum.Granted
        );
        Assert.True(firstCreated);

        // An operator renames the discovered client.
        DnsClientEntity client = await _clientRepository.GetByClientIdentifierAsync(
            clientIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        client.DisplayName = "Operator-Renamed Device";
        _clientRepository.Update(client);
        await _unitOfWork.SaveChangesAsync();

        // The device renews: the same lease row is updated in place (one lease per scope and
        // client) with a different host name, and the event fires again for that lease.
        LeaseEntity leaseToRenew = await _leaseRepository.GetByIdAsync(firstLeaseId);
        leaseToRenew.HostName = "totally-different-name";
        _leaseRepository.Update(leaseToRenew);
        await _unitOfWork.SaveChangesAsync();

        bool secondCreated = await CreateService()
            .HandleLeaseChangeAsync(firstLeaseId, DhcpLeaseChangeKindEnum.Granted);

        // No new row was created, and the operator's name is unchanged.
        Assert.False(secondCreated);
        DnsClientEntity afterRenewal = await _clientRepository.GetByClientIdentifierAsync(
            clientIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        Assert.Equal("Operator-Renamed Device", afterRenewal.DisplayName);
    }

    [Theory]
    [InlineData(DhcpLeaseChangeKindEnum.Released)]
    [InlineData(DhcpLeaseChangeKindEnum.Expired)]
    public async Task NonGrantChangeKinds_NeverCreateAKnownClientRow(
        DhcpLeaseChangeKindEnum changeKind
    )
    {
        const string clientIdentifier = "AA:BB:CC:DD:EE:04";
        Guid leaseId = await SeedGrantedLeaseAsync(clientIdentifier, "workstation-4");

        bool created = await CreateService().HandleLeaseChangeAsync(leaseId, changeKind);

        Assert.False(created);
        DnsClientEntity client = await _clientRepository.GetByClientIdentifierAsync(
            clientIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        Assert.Null(client);
    }

    [Fact]
    public async Task GrantedLease_ForANonexistentLease_DoesNothing()
    {
        bool created = await CreateService()
            .HandleLeaseChangeAsync(Guid.NewGuid(), DhcpLeaseChangeKindEnum.Granted);

        Assert.False(created);
    }

    /// <summary>
    ///     This method grants a lease and runs auto-population for it, returning its result.
    /// </summary>
    private async Task<bool> GrantAsync(
        string clientIdentifier,
        string hostName,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        Guid leaseId = await SeedGrantedLeaseAsync(clientIdentifier, hostName, protocolVersion);
        return await CreateService()
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);
    }

    [Fact]
    public async Task GrantedDhcpv6Lease_WhoseDuidEmbedsAKnownMac_JoinsThatDeviceInsteadOfDuplicatingIt()
    {
        const string mac = "aa:bb:cc:dd:ee:10";
        const string duidLlt = "00:01:00:01:2a:bc:de:f0:" + mac;

        Assert.True(await GrantAsync(mac, "laptop", InternetProtocolVersionEnum.IPv4));
        Assert.True(await GrantAsync(duidLlt, "laptop", InternetProtocolVersionEnum.IPv6));

        DnsClientEntity viaV4 = await _clientRepository.GetByClientIdentifierAsync(
            mac,
            InternetProtocolVersionEnum.IPv4
        );
        DnsClientEntity viaV6 = await _clientRepository.GetByClientIdentifierAsync(
            duidLlt,
            InternetProtocolVersionEnum.IPv6
        );
        Assert.Equal(viaV4.Id, viaV6.Id);
        Assert.Single(await _clientRepository.ListAsync());
        Assert.Equal(2, (await _clientAddressRepository.ListByClientIdAsync(viaV4.Id)).Count);
    }

    [Fact]
    public async Task GrantedDhcpv4Lease_MatchingAKnownDuidLl_JoinsAndFillsAMissingDisplayName()
    {
        const string mac = "aa:bb:cc:dd:ee:11";

        // The DHCPv6 lease arrives first, with no host name.
        Assert.True(await GrantAsync("00:03:00:01:" + mac, null, InternetProtocolVersionEnum.IPv6));
        Assert.True(await GrantAsync(mac, "printer", InternetProtocolVersionEnum.IPv4));

        DnsClientEntity device = Assert.Single(await _clientRepository.ListAsync());
        Assert.Equal("printer", device.DisplayName);
    }

    [Fact]
    public async Task LinkingANewIdentifier_NeverOverwritesAnExistingDisplayName()
    {
        const string mac = "aa:bb:cc:dd:ee:12";

        Assert.True(await GrantAsync(mac, "nas", InternetProtocolVersionEnum.IPv4));
        Assert.True(
            await GrantAsync("00:03:00:01:" + mac, "nas-v6-name", InternetProtocolVersionEnum.IPv6)
        );

        DnsClientEntity device = Assert.Single(await _clientRepository.ListAsync());
        Assert.Equal("nas", device.DisplayName);
    }

    [Fact]
    public async Task GrantedDhcpv6Lease_ForADifferentMac_CreatesASeparateDevice()
    {
        Assert.True(await GrantAsync("aa:bb:cc:dd:ee:13", "a", InternetProtocolVersionEnum.IPv4));
        Assert.True(
            await GrantAsync("00:03:00:01:aa:bb:cc:dd:ee:14", "b", InternetProtocolVersionEnum.IPv6)
        );

        Assert.Equal(2, (await _clientRepository.ListAsync()).Count);
    }

    [Fact]
    public async Task GrantedLease_MatchingMoreThanOneDevice_CreatesANewDeviceRatherThanGuessing()
    {
        const string mac = "aa:bb:cc:dd:ee:15";

        // Two separately-registered devices both carry this MAC: a plain chaddr, and an RFC 2132
        // hardware-type-prefixed Client-identifier.
        Assert.True(await GrantAsync(mac, "one", InternetProtocolVersionEnum.IPv4));
        await SeedDeviceAsync("01:" + mac, InternetProtocolVersionEnum.IPv4);

        Assert.True(
            await GrantAsync("00:03:00:01:" + mac, "three", InternetProtocolVersionEnum.IPv6)
        );

        Assert.Equal(3, (await _clientRepository.ListAsync()).Count);
    }

    /// <summary>
    ///     This method registers a device directly, without auto-population.
    /// </summary>
    private async Task SeedDeviceAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DnsClientEntity device = new() { Id = Guid.NewGuid(), CreatedAtUtc = now };
        await _clientRepository.AddAsync(device);
        await _clientAddressRepository.AddAsync(
            new DnsClientAddressEntity
            {
                Id = Guid.NewGuid(),
                ClientId = device.Id,
                ClientIdentifier = clientIdentifier,
                ProtocolVersion = protocolVersion,
                FirstSeenUtc = now,
                CreatedAtUtc = now,
            }
        );
        await _unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task EveryGrant_IncludingARenewalOfAKnownDevice_IsRecordedAsAPresenceSighting()
    {
        IdentityRecorder recorder = new();
        const string mac = "aa:bb:cc:dd:ee:20";

        // A renewal grants the same lease row again.
        Guid leaseId = await SeedGrantedLeaseAsync(mac, "tv");
        await CreateService(recorder)
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);
        await CreateService(recorder)
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);

        Assert.Equal(2, recorder.Sightings.Count);
        Assert.All(recorder.Sightings, sighting => Assert.Equal(mac, sighting.Identifier));
        Assert.All(recorder.Sightings, sighting => Assert.NotNull(sighting.Address));
    }

    [Fact]
    public async Task APrefixDelegationGrant_RecordsTheIdentityButNotTheDelegatedPrefix()
    {
        IdentityRecorder recorder = new();
        Guid leaseId = Guid.NewGuid();
        await _leaseRepository.AddAsync(
            new LeaseEntity
            {
                Id = leaseId,
                ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                ScopeName = "lan",
                ClientIdentifier = "00:03:00:01:aa:bb:cc:dd:ee:21",
                IpAddress = "2001:db8:100::",
                PrefixLength = 56,
                State = LeaseStateEnum.Active,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await CreateService(recorder)
            .HandleLeaseChangeAsync(leaseId, DhcpLeaseChangeKindEnum.Granted);

        Assert.Null(Assert.Single(recorder.Sightings).Address);
    }

    /// <summary>
    ///     This class records every identity sighting it is handed.
    /// </summary>
    private sealed class IdentityRecorder : IClientPresenceRecorder
    {
        /// <summary>Every identity sighting recorded.</summary>
        public List<(string Identifier, System.Net.IPAddress Address)> Sightings { get; } = [];

        /// <inheritdoc />
        public void RecordIdentity(
            string clientIdentifier,
            InternetProtocolVersionEnum protocolVersion,
            System.Net.IPAddress address,
            DateTimeOffset seenAtUtc
        ) => Sightings.Add((clientIdentifier, address));

        /// <inheritdoc />
        public void RecordHardwareAddress(
            byte[] macAddress,
            System.Net.IPAddress address,
            DateTimeOffset seenAtUtc
        ) { }

        /// <inheritdoc />
        public void RecordSourceAddress(System.Net.IPAddress address, DateTimeOffset seenAtUtc) { }
    }
}
