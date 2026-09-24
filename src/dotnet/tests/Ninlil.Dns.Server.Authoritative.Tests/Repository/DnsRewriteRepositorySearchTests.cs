using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsRewriteRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13i proof that <c>DnsRewritesController</c>'s own
///     rewrite listing no longer materializes every rewrite into memory before slicing a page out
///     of it, and that the real domain-then-creation-time ordering <c>DnsRewritesController</c>
///     already applied in-memory survives the move to a real <c>ORDER BY</c>, mirroring
///     <see cref="DnsForwarderRepositorySearchTests" />'s own Phase 13h two-key ordering proof for
///     the same shape. This class's own passing runs are also the live proof that
///     <c>DnsRewriteConfiguration</c>'s new <c>CreatedAtUtc</c>-as-UTC-ticks conversion actually
///     fixes the exact SQLite <c>ORDER BY</c>-over-<c>DateTimeOffset</c> failure Phase 13h's own
///     <c>DnssecKeyRepositorySearchTests</c> first caught for the identical shape of problem --
///     without that conversion, this class's own tests fail with a real
///     <see cref="System.NotSupportedException" /> from SQLite's own EF Core provider, not merely
///     an assertion mismatch.
/// </summary>
public sealed class DnsRewriteRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five rewrites with a deliberate mix of <c>Domain</c> ties (broken by
    ///     <c>CreatedAtUtc</c>) and out-of-order creation, so a passing ordering assertion can
    ///     only be explained by a real, translated two-key <c>ORDER BY</c>, not by creation order
    ///     or a single sort key happening to already match.
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
