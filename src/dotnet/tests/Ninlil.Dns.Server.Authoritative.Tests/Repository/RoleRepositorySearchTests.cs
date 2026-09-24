using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.RoleRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13j proof that <c>RolesController</c>'s own role
///     listing no longer materializes every role into memory before slicing a page out of it,
///     mirroring <see cref="UserRepositorySearchTests" />'s own identical proof for
///     <see cref="IUserRepository" />. See that class's own doc comment for why this uses
///     <see cref="RepositorySearchTestHost" /> directly rather than
///     <c>Ninlil.Dns.Identity.Tests</c>'s own test host.
/// </summary>
public sealed class RoleRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five roles with names deliberately out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by a real <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order happening to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-role",
            "bravo-role",
            "echo-role",
            "alpha-role",
            "charlie-role",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new RoleEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryRoleOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        IReadOnlyList<RoleEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-role", "bravo-role", "charlie-role", "delta-role", "echo-role"],
            [.. results.Select(role => role.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        // Page size 2 over 5 roles: page 0 gets alpha/bravo, page 1 gets charlie/delta, page 2
        // gets only echo (the final, partial page) -- proving skip/take are genuinely applied at
        // the database, not merely accepted and ignored.
        IReadOnlyList<RoleEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<RoleEntity> secondPage = await repository.SearchAsync(skip: 2, take: 2);
        IReadOnlyList<RoleEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(["alpha-role", "bravo-role"], [.. firstPage.Select(r => r.Name)]);
        Assert.Equal(["charlie-role", "delta-role"], [.. secondPage.Select(r => r.Name)]);
        Assert.Equal(["echo-role"], [.. thirdPage.Select(r => r.Name)]);

        // No two pages ever overlap.
        Assert.DoesNotContain(secondPage, role => firstPage.Any(first => first.Id == role.Id));
        Assert.DoesNotContain(thirdPage, role => secondPage.Any(second => second.Id == role.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullRoleCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<RoleEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        IReadOnlyList<RoleEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }
}
