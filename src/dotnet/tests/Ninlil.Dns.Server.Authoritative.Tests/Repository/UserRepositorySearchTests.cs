using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.UserRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>UsersController</c>'s listing needs, rather
///     than in memory. It tests
///     <see cref="IUserRepository" /> directly with the EF Core registration, not through
///     <c>UserManager&lt;TUser&gt;</c> or <c>Ninlil.Dns.Identity.Tests</c>'s host, because paging is
///     a storage concern.
/// </summary>
public sealed class UserRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five user accounts with sign-in names out of
    ///     alphabetical creation order, so a passing ordering assertion can only be explained by a
    ///     real <c>ORDER BY</c> translated at the database, not by creation/insertion order
    ///     already matching.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] userNamesInSeedOrder = ["delta", "bravo", "echo", "alpha", "charlie"];

        foreach (string userName in userNamesInSeedOrder)
            await repository.AddAsync(
                new UserEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UserName = userName,
                    NormalizedUserName = userName.ToUpperInvariant(),
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryUserOrderedByUserNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        IReadOnlyList<UserEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha", "bravo", "charlie", "delta", "echo"],
            [.. results.Select(user => user.UserName)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        // Page size 2 over 5 users: page 0 gets alpha/bravo, page 1 gets charlie/delta, page 2
        // gets only echo (the final, partial page) -- proving skip/take are applied at
        // the database, not merely accepted and ignored.
        IReadOnlyList<UserEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<UserEntity> secondPage = await repository.SearchAsync(skip: 2, take: 2);
        IReadOnlyList<UserEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(["alpha", "bravo"], [.. firstPage.Select(u => u.UserName)]);
        Assert.Equal(["charlie", "delta"], [.. secondPage.Select(u => u.UserName)]);
        Assert.Equal(["echo"], [.. thirdPage.Select(u => u.UserName)]);

        // No two pages ever overlap.
        Assert.DoesNotContain(secondPage, user => firstPage.Any(first => first.Id == user.Id));
        Assert.DoesNotContain(thirdPage, user => secondPage.Any(second => second.Id == user.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullUserCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<UserEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        IReadOnlyList<UserEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }
}
