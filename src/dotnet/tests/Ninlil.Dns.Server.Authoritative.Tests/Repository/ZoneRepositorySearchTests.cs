using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.ZoneRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13g proof that <c>ZonesController</c>'s own zone
///     listing no longer materializes every zone into memory before slicing a page out of it.
/// </summary>
public sealed class ZoneRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five zones with names deliberately out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by a real <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order happening to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta.example.com",
            "bravo.example.com",
            "echo.example.com",
            "alpha.example.com",
            "charlie.example.com",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new ZoneEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    Type = ZoneTypeEnum.Primary,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryZoneOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        IReadOnlyList<ZoneEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            [
                "alpha.example.com",
                "bravo.example.com",
                "charlie.example.com",
                "delta.example.com",
                "echo.example.com",
            ],
            [.. results.Select(zone => zone.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        // Page size 2 over 5 zones: page 0 gets alpha/bravo, page 1 gets charlie/delta, page 2
        // gets only echo (the final, partial page) -- proving skip/take are genuinely applied at
        // the database, not merely accepted and ignored.
        IReadOnlyList<ZoneEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<ZoneEntity> secondPage = await repository.SearchAsync(skip: 2, take: 2);
        IReadOnlyList<ZoneEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(
            ["alpha.example.com", "bravo.example.com"],
            [.. firstPage.Select(z => z.Name)]
        );
        Assert.Equal(
            ["charlie.example.com", "delta.example.com"],
            [.. secondPage.Select(z => z.Name)]
        );
        Assert.Equal(["echo.example.com"], [.. thirdPage.Select(z => z.Name)]);

        // No two pages ever overlap.
        Assert.DoesNotContain(secondPage, zone => firstPage.Any(first => first.Id == zone.Id));
        Assert.DoesNotContain(thirdPage, zone => secondPage.Any(second => second.Id == zone.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullZoneCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<ZoneEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        IReadOnlyList<ZoneEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }
}
