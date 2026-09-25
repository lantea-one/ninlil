using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientGroupNetworkRepository" /> (<c>SearchByGroupIdAsync</c>, <c>CountByGroupIdAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>ClientGroupNetworksController</c>'s listing needs, rather
///     than in memory. It also
///     checks that another group's networks never appear.
/// </summary>
public sealed class ClientGroupNetworkRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one target group's five networks, out of CIDR creation
    ///     order, plus one extra network on a different group, so a passing
    ///     group-scoping assertion can only be explained by a translated
    ///     <c>WHERE ClientGroupId = ...</c> clause, not by the target group simply happening to be
    ///     the only group with any networks at all.
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid targetGroupId = Guid.NewGuid();
        Guid otherGroupId = Guid.NewGuid();

        string[] cidrsInSeedOrder =
        [
            "10.0.4.0/24",
            "10.0.2.0/24",
            "10.0.5.0/24",
            "10.0.1.0/24",
            "10.0.3.0/24",
        ];

        foreach (string cidr in cidrsInSeedOrder)
            await repository.AddAsync(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClientGroupId = targetGroupId,
                    Cidr = cidr,
                }
            );

        await repository.AddAsync(
            new DnsClientGroupNetworkEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ClientGroupId = otherGroupId,
                Cidr = "192.168.0.0/24",
            }
        );

        await unitOfWork.SaveChangesAsync();

        return targetGroupId;
    }

    [Fact]
    public async Task SearchByGroupIdAsync_ReturnsOnlyTheTargetGroupsNetworks_OrderedByCidr()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();

        IReadOnlyList<DnsClientGroupNetworkEntity> results = await repository.SearchByGroupIdAsync(
            targetGroupId,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.All(results, network => Assert.Equal(targetGroupId, network.ClientGroupId));
        Assert.Equal(
            ["10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24", "10.0.4.0/24", "10.0.5.0/24"],
            [.. results.Select(network => network.Cidr)]
        );
    }

    [Fact]
    public async Task SearchByGroupIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();

        IReadOnlyList<DnsClientGroupNetworkEntity> firstPage =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 0, take: 2);
        IReadOnlyList<DnsClientGroupNetworkEntity> thirdPage =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 4, take: 2);

        Assert.Equal(2, firstPage.Count);
        Assert.Single(thirdPage);
        Assert.DoesNotContain(thirdPage, network => firstPage.Any(first => first.Id == network.Id));
    }

    [Fact]
    public async Task CountByGroupIdAsync_ReflectsOnlyTheTargetGroupsNetworkCount()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();

        int count = await repository.CountByGroupIdAsync(targetGroupId);

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task SearchByGroupIdAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();

        IReadOnlyList<DnsClientGroupNetworkEntity> results = await repository.SearchByGroupIdAsync(
            targetGroupId,
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
