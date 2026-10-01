using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Tests.Repository;

/// <summary>
///     This class covers the <c>SearchByScopeIdAsync</c> and <c>CountByScopeIdAsync</c> paging
///     and the client and address lookups of
///     <see cref="Ninlil.Dhcp.Data.EntityFrameworkCore.Repository.DhcpReservationRepository" />
///     against a temporary SQLite database file (<see cref="DhcpRepositorySearchTestHost" />),
///     including that another scope's reservations never appear in a scope's page, as
///     <c>RecordRepositorySearchTests</c> does for zone records.
/// </summary>
public sealed class DhcpReservationRepositorySearchTests : IAsyncLifetime
{
    private readonly DhcpRepositorySearchTestHost _testHost = new();
    private Guid _scopeId;
    private Guid _otherScopeId;

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one scope with five reservations inserted out of client-identifier
    ///     order, plus another scope with one reservation.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository scopeRepository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        IDhcpReservationRepository reservationRepository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _scopeId = Guid.NewGuid();
        _otherScopeId = Guid.NewGuid();

        await scopeRepository.AddAsync(NewScope(_scopeId, "reservations-scope"));
        await scopeRepository.AddAsync(NewScope(_otherScopeId, "other-scope"));

        string[] clientIdentifiersInSeedOrder =
        [
            "00:00:00:00:00:04",
            "00:00:00:00:00:02",
            "00:00:00:00:00:05",
            "00:00:00:00:00:01",
            "00:00:00:00:00:03",
        ];

        int lastOctet = 10;
        foreach (string clientIdentifier in clientIdentifiersInSeedOrder)
            await reservationRepository.AddAsync(
                NewReservation(_scopeId, clientIdentifier, $"192.0.2.{lastOctet++}")
            );

        // A reservation in a different scope, which must not appear in a search scoped to _scopeId.
        await reservationRepository.AddAsync(
            NewReservation(_otherScopeId, "00:00:00:00:00:99", "192.0.2.99")
        );

        await unitOfWork.SaveChangesAsync();
    }

    private static DhcpScopeEntity NewScope(Guid id, string name) =>
        new()
        {
            Id = id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = "192.0.2.10",
            RangeEndAddress = "192.0.2.200",
            SubnetPrefixLength = 24,
        };

    private static DhcpReservationEntity NewReservation(
        Guid scopeId,
        string clientIdentifier,
        string ipAddress
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ScopeId = scopeId,
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
        };

    [Fact]
    public async Task SearchByScopeIdAsync_ReturnsOnlyThatScopesReservationsOrderedByClientIdentifier()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpReservationRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();

        IReadOnlyList<DhcpReservationEntity> results = await repository.SearchByScopeIdAsync(
            _scopeId,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.All(results, reservation => Assert.Equal(_scopeId, reservation.ScopeId));
        Assert.Equal(
            [
                "00:00:00:00:00:01",
                "00:00:00:00:00:02",
                "00:00:00:00:00:03",
                "00:00:00:00:00:04",
                "00:00:00:00:00:05",
            ],
            [.. results.Select(reservation => reservation.ClientIdentifier)]
        );
    }

    [Fact]
    public async Task SearchByScopeIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpReservationRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();

        IReadOnlyList<DhcpReservationEntity> firstPage = await repository.SearchByScopeIdAsync(
            _scopeId,
            skip: 0,
            take: 2
        );
        IReadOnlyList<DhcpReservationEntity> secondPage = await repository.SearchByScopeIdAsync(
            _scopeId,
            skip: 2,
            take: 2
        );

        Assert.Equal(
            ["00:00:00:00:00:01", "00:00:00:00:00:02"],
            [.. firstPage.Select(reservation => reservation.ClientIdentifier)]
        );
        Assert.Equal(
            ["00:00:00:00:00:03", "00:00:00:00:00:04"],
            [.. secondPage.Select(reservation => reservation.ClientIdentifier)]
        );
        Assert.DoesNotContain(
            secondPage,
            reservation => firstPage.Any(first => first.Id == reservation.Id)
        );
    }

    [Fact]
    public async Task CountByScopeIdAsync_CountsOnlyThatScopesReservations()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpReservationRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();

        int scopeCount = await repository.CountByScopeIdAsync(_scopeId);
        int otherScopeCount = await repository.CountByScopeIdAsync(_otherScopeId);

        Assert.Equal(5, scopeCount);
        Assert.Equal(1, otherScopeCount);
    }

    [Fact]
    public async Task FindByClientIdentifierAsync_And_FindByIpAddressAsync_ReturnTheMatchingReservation()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpReservationRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();

        DhcpReservationEntity byClient = await repository.FindByClientIdentifierAsync(
            _scopeId,
            "00:00:00:00:00:03"
        );
        Assert.NotNull(byClient);

        // Look up the address just found rather than a hand-computed one, so the test cannot
        // drift from SeedAsync's identifier-to-address assignment.
        DhcpReservationEntity byAddress = await repository.FindByIpAddressAsync(
            _scopeId,
            byClient.IpAddress
        );
        DhcpReservationEntity notFoundInOtherScope = await repository.FindByClientIdentifierAsync(
            _otherScopeId,
            "00:00:00:00:00:03"
        );

        Assert.NotNull(byAddress);
        Assert.Equal(byClient.Id, byAddress.Id);
        Assert.Equal("00:00:00:00:00:03", byAddress.ClientIdentifier);
        Assert.Null(notFoundInOtherScope);
    }
}
