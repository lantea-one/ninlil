using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsFilterListRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>FilterListsController</c>'s listing needs, rather
///     than in memory.
/// </summary>
public sealed class DnsFilterListRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five lists with names out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by an <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order already matching.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-list",
            "bravo-list",
            "echo-list",
            "alpha-list",
            "charlie-list",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new DnsFilterListEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    Kind = DnsFilterListKindEnum.Domain,
                    Action = DnsFilterListActionEnum.Block,
                    Source = DnsFilterListSourceEnum.Local,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryListOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsFilterListEntity> results = await repository.SearchAsync(
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-list", "bravo-list", "charlie-list", "delta-list", "echo-list"],
            [.. results.Select(list => list.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsFilterListEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsFilterListEntity> secondPage = await repository.SearchAsync(
            skip: 2,
            take: 2
        );
        IReadOnlyList<DnsFilterListEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(["alpha-list", "bravo-list"], [.. firstPage.Select(l => l.Name)]);
        Assert.Equal(["charlie-list", "delta-list"], [.. secondPage.Select(l => l.Name)]);
        Assert.Equal(["echo-list"], [.. thirdPage.Select(l => l.Name)]);

        Assert.DoesNotContain(secondPage, list => firstPage.Any(first => first.Id == list.Id));
        Assert.DoesNotContain(thirdPage, list => secondPage.Any(second => second.Id == list.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullListCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DnsFilterListEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsFilterListEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
