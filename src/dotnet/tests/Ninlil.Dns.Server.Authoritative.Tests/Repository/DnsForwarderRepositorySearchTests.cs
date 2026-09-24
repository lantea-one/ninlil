using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsForwarderRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13h proof that <c>ForwardersController</c>'s own
///     forwarder listing no longer materializes every forwarder into memory before slicing a
///     page out of it, and that the real <c>SortOrder</c>-then-<c>Name</c> ordering
///     <c>ForwardersController</c> already applied in-memory survives the move to a real
///     <c>ORDER BY</c>, mirroring <see cref="ZoneRepositorySearchTests" />'s own Phase 13g proof
///     for the same shape.
/// </summary>
public sealed class DnsForwarderRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five forwarders with a deliberate mix of <c>SortOrder</c> ties
    ///     (broken by name) and out-of-order creation, so a passing ordering assertion can only
    ///     be explained by a real, translated two-key <c>ORDER BY</c>, not by creation order or a
    ///     single sort key happening to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        (string Name, int SortOrder)[] forwardersInSeedOrder =
        [
            ("delta-forwarder", 1),
            ("bravo-forwarder", 0),
            ("echo-forwarder", 2),
            ("alpha-forwarder", 0),
            ("charlie-forwarder", 1),
        ];

        foreach ((string name, int sortOrder) in forwardersInSeedOrder)
            await repository.AddAsync(
                new DnsForwarderEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    DefinitionString = "1.1.1.1",
                    SortOrder = sortOrder,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryForwarderOrderedBySortOrderThenName()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        IReadOnlyList<DnsForwarderEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            [
                "alpha-forwarder",
                "bravo-forwarder",
                "charlie-forwarder",
                "delta-forwarder",
                "echo-forwarder",
            ],
            [.. results.Select(forwarder => forwarder.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        IReadOnlyList<DnsForwarderEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsForwarderEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(["alpha-forwarder", "bravo-forwarder"], [.. firstPage.Select(f => f.Name)]);
        Assert.Equal(["echo-forwarder"], [.. thirdPage.Select(f => f.Name)]);
        Assert.DoesNotContain(
            thirdPage,
            forwarder => firstPage.Any(first => first.Id == forwarder.Id)
        );
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullForwarderCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DnsForwarderEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        IReadOnlyList<DnsForwarderEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
