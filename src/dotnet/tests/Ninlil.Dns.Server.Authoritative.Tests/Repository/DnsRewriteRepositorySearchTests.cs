using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsRewriteRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>DnsRewritesController</c>'s listing needs, rather
///     than in memory. It also
///     checks the domain-then-creation-time ordering. SQLite can only sort <c>CreatedAtUtc</c>
///     because <c>DnsRewriteConfiguration</c> stores it as UTC ticks; without that conversion these
///     tests throw <see cref="System.NotSupportedException" />.
/// </summary>
public sealed class DnsRewriteRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five rewrites with a deliberate mix of <c>Domain</c> ties (broken by
    ///     <c>CreatedAtUtc</c>) and out-of-order creation, so a passing ordering assertion can
    ///     only be explained by a translated two-key <c>ORDER BY</c>, not by creation order
    ///     or a single sort key already matching.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset baseTime = DateTimeOffset.UtcNow;

        (string Domain, int OffsetMinutes)[] rewritesInSeedOrder =
        [
            ("delta.example.com", 40),
            ("bravo.example.com", 10),
            ("bravo.example.com", 0),
            ("alpha.example.com", 30),
            ("charlie.example.com", 20),
        ];

        foreach ((string domain, int offsetMinutes) in rewritesInSeedOrder)
            await repository.AddAsync(
                new DnsRewriteEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = baseTime.AddMinutes(offsetMinutes),
                    Domain = domain,
                    Target = "192.0.2.1",
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryRewriteOrderedByDomainThenCreatedAt()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();

        IReadOnlyList<DnsRewriteEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);

        // "bravo.example.com" ties on domain; the entry created earlier (offset 0) must sort
        // before the one created later (offset 10).
        Assert.Equal(
            [
                "alpha.example.com",
                "bravo.example.com",
                "bravo.example.com",
                "charlie.example.com",
                "delta.example.com",
            ],
            [.. results.Select(rewrite => rewrite.Domain)]
        );
        Assert.True(results[1].CreatedAtUtc < results[2].CreatedAtUtc);
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();

        IReadOnlyList<DnsRewriteEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<DnsRewriteEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(2, firstPage.Count);
        Assert.Single(thirdPage);
        Assert.DoesNotContain(thirdPage, rewrite => firstPage.Any(first => first.Id == rewrite.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullRewriteCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DnsRewriteEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();

        IReadOnlyList<DnsRewriteEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }
}
