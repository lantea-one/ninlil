using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientGroupRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>ClientGroupsController</c>'s listing needs, rather
///     than in memory.
/// </summary>
public sealed class ClientGroupRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five client groups with names out of alphabetical
    ///     creation order, so a passing ordering assertion can only be explained by a real
    ///     <c>ORDER BY</c> translated at the database, not by creation/insertion order happening
    ///     to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-group",
            "bravo-group",
            "echo-group",
            "alpha-group",
            "charlie-group",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new DnsClientGroupEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryGroupOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();

        IReadOnlyList<DnsClientGroupEntity> results = await repository.SearchAsync(
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-group", "bravo-group", "charlie-group", "delta-group", "echo-group"],
            [.. results.Select(group => group.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();

        // Page size 2 over 5 groups: page 0 gets alpha/bravo, page 1 gets charlie/delta, page 2
        // gets only echo (the final, partial page) -- proving skip/take are applied at
        // the database, not merely accepted and ignored.
        IReadOnlyList<DnsClientGroupEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsClientGroupEntity> secondPage = await repository.SearchAsync(
            skip: 2,
            take: 2
        );
        IReadOnlyList<DnsClientGroupEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(["alpha-group", "bravo-group"], [.. firstPage.Select(g => g.Name)]);
        Assert.Equal(["charlie-group", "delta-group"], [.. secondPage.Select(g => g.Name)]);
        Assert.Equal(["echo-group"], [.. thirdPage.Select(g => g.Name)]);

        // No two pages ever overlap.
        Assert.DoesNotContain(secondPage, group => firstPage.Any(first => first.Id == group.Id));
        Assert.DoesNotContain(thirdPage, group => secondPage.Any(second => second.Id == group.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullGroupCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DnsClientGroupEntity> onePage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();

        IReadOnlyList<DnsClientGroupEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
