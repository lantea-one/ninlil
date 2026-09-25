using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.ClusterNodeRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>ClusterController</c>'s listing needs, rather
///     than in memory.
/// </summary>
public sealed class ClusterNodeRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five peers with names out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by an <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order already matching.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-node",
            "bravo-node",
            "echo-node",
            "alpha-node",
            "charlie-node",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new ClusterNodeEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    RemoteNodeId = Guid.NewGuid(),
                    Host = "192.0.2.1",
                    Port = 443,
                    TsigKeyName = "cluster-key.",
                    TlsCertificateThumbprint = "abc123",
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryNodeOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-node", "bravo-node", "charlie-node", "delta-node", "echo-node"],
            [.. results.Select(node => node.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<ClusterNodeEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(["alpha-node", "bravo-node"], [.. firstPage.Select(n => n.Name)]);
        Assert.Equal(["echo-node"], [.. thirdPage.Select(n => n.Name)]);
        Assert.DoesNotContain(thirdPage, node => firstPage.Any(first => first.Id == node.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullNodeCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<ClusterNodeEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
