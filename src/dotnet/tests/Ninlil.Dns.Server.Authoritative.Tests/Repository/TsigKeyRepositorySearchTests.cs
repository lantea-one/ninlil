using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.TsigKeyRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13h proof that <c>TsigKeysController</c>'s own key
///     listing no longer materializes every key into memory before slicing a page out of it,
///     mirroring <see cref="ZoneRepositorySearchTests" />'s own Phase 13g proof for the same
///     shape.
/// </summary>
public sealed class TsigKeyRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five keys with names deliberately out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by a real <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order happening to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-key",
            "bravo-key",
            "echo-key",
            "alpha-key",
            "charlie-key",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    AlgorithmName = "hmac-sha256.",
                    SecretEncrypted = "encrypted-secret",
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryKeyOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

        IReadOnlyList<TsigKeyEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-key", "bravo-key", "charlie-key", "delta-key", "echo-key"],
            [.. results.Select(key => key.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

        IReadOnlyList<TsigKeyEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<TsigKeyEntity> secondPage = await repository.SearchAsync(skip: 2, take: 2);
        IReadOnlyList<TsigKeyEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(["alpha-key", "bravo-key"], [.. firstPage.Select(k => k.Name)]);
        Assert.Equal(["charlie-key", "delta-key"], [.. secondPage.Select(k => k.Name)]);
        Assert.Equal(["echo-key"], [.. thirdPage.Select(k => k.Name)]);

        Assert.DoesNotContain(secondPage, key => firstPage.Any(first => first.Id == key.Id));
        Assert.DoesNotContain(thirdPage, key => secondPage.Any(second => second.Id == key.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullKeyCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<TsigKeyEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

        IReadOnlyList<TsigKeyEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }
}
