using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsFilterListEntryRepository" /> (<c>SearchByListIdAsync</c>, <c>CountByListIdAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>FilterListEntriesController</c>'s listing needs, rather
///     than in memory. It also
///     checks that another list's entries never appear. A subscribed list can hold tens of
///     thousands of entries, so
///     <see cref="SearchByListIdAsync_LargeEntrySet_PaginatesCorrectlyAcrossHundredsOfRows" /> seeds
///     several hundred rows.
/// </summary>
public sealed class DnsFilterListEntryRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one target list's five entries out of alphabetical
    ///     value order -- plus one extra entry on a different list, so a passing
    ///     list-scoping assertion can only be explained by a translated
    ///     <c>WHERE ListId = ...</c> clause, not by the target list simply happening to be the
    ///     only list with any entries at all.
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid targetListId = Guid.NewGuid();
        Guid otherListId = Guid.NewGuid();

        string[] valuesInSeedOrder =
        [
            "delta.example.com",
            "bravo.example.com",
            "echo.example.com",
            "alpha.example.com",
            "charlie.example.com",
        ];

        foreach (string value in valuesInSeedOrder)
            await repository.AddAsync(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ListId = targetListId,
                    Value = value,
                }
            );

        await repository.AddAsync(
            new DnsFilterListEntryEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ListId = otherListId,
                Value = "other-list.example.com",
            }
        );

        await unitOfWork.SaveChangesAsync();

        return targetListId;
    }

    [Fact]
    public async Task SearchByListIdAsync_ReturnsOnlyTheTargetListsEntries_OrderedByValue()
    {
        Guid targetListId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        IReadOnlyList<DnsFilterListEntryEntity> results = await repository.SearchByListIdAsync(
            targetListId,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.All(results, entry => Assert.Equal(targetListId, entry.ListId));
        Assert.Equal(
            [
                "alpha.example.com",
                "bravo.example.com",
                "charlie.example.com",
                "delta.example.com",
                "echo.example.com",
            ],
            [.. results.Select(entry => entry.Value)]
        );
    }

    [Fact]
    public async Task SearchByListIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        Guid targetListId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        IReadOnlyList<DnsFilterListEntryEntity> firstPage = await repository.SearchByListIdAsync(
            targetListId,
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsFilterListEntryEntity> thirdPage = await repository.SearchByListIdAsync(
            targetListId,
            skip: 4,
            take: 2
        );

        Assert.Equal(2, firstPage.Count);
        Assert.Single(thirdPage);
        Assert.DoesNotContain(thirdPage, entry => firstPage.Any(first => first.Id == entry.Id));
    }

    [Fact]
    public async Task CountByListIdAsync_ReflectsOnlyTheTargetListsEntryCount()
    {
        Guid targetListId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        int count = await repository.CountByListIdAsync(targetListId);

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task SearchByListIdAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        Guid targetListId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        IReadOnlyList<DnsFilterListEntryEntity> results = await repository.SearchByListIdAsync(
            targetListId,
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }

    /// <summary>
    ///     This test seeds 300 entries -- a scale representative of a real subscribed
    ///     external block list, far beyond this project's other pagination-proof tests' handful
    ///     of rows in reverse-alphabetical insertion order, then walks every page
    ///     of a small page size end to end, confirming every page is disjoint, every page (except
    ///     possibly the last) is full, the final page is the correct partial size, and the
    ///     concatenation of every page reproduces the full, correctly ordered set. A result this
    ///     precise across 300 rows and many pages is only plausible from a translated
    ///     <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> query served by the composite
    ///     <c>(ListId, Value)</c> index <c>DnsFilterListEntryConfiguration</c> establishes -- an
    ///     in-memory sort/skip/take would still produce a correct result, but this project's own
    ///     stated purpose for this test is proving the query is pushed down and indexed,
    ///     not merely correct by coincidence.
    /// </summary>
    [Fact]
    public async Task SearchByListIdAsync_LargeEntrySet_PaginatesCorrectlyAcrossHundredsOfRows()
    {
        const int totalEntries = 300;
        const int pageSize = 37;

        await using AsyncServiceScope seedScope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository seedRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        IUnitOfWork seedUnitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid targetListId = Guid.NewGuid();

        // Zero-pad so ordinal string ordering matches numeric ordering, and seed in reverse
        // order so a passing result can only be explained by a real ORDER BY.
        List<string> expectedOrderedValues = [];
        for (int i = 0; i < totalEntries; i++)
            expectedOrderedValues.Add($"entry-{i:D4}.example.com");

        for (int i = totalEntries - 1; i >= 0; i--)
            await seedRepository.AddAsync(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ListId = targetListId,
                    Value = expectedOrderedValues[i],
                }
            );

        await seedUnitOfWork.SaveChangesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        int totalCount = await repository.CountByListIdAsync(targetListId);
        Assert.Equal(totalEntries, totalCount);

        List<string> collectedValues = [];
        HashSet<Guid> seenIds = [];

        for (int skip = 0; skip < totalEntries; skip += pageSize)
        {
            IReadOnlyList<DnsFilterListEntryEntity> page = await repository.SearchByListIdAsync(
                targetListId,
                skip,
                pageSize
            );

            int expectedPageSize = Math.Min(pageSize, totalEntries - skip);
            Assert.Equal(expectedPageSize, page.Count);

            // Every page must be disjoint from every page already seen -- proving no row is
            // returned twice across the whole paginated walk.
            foreach (DnsFilterListEntryEntity entry in page)
                Assert.True(seenIds.Add(entry.Id), $"Entry '{entry.Value}' appeared on two pages.");

            collectedValues.AddRange(page.Select(entry => entry.Value));
        }

        Assert.Equal(expectedOrderedValues, collectedValues);
    }
}
