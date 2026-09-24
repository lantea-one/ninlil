using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dhcp.Data.EntityFrameworkCore.Repository.DhcpScopeRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database, proving from this sub-phase's own first commit that a future
///     scope-listing REST action never has to materialize every scope into memory before slicing
///     a page out of it -- the exact mistake this project's own DNS-side repositories originally
///     made and later had to sweep back through.
/// </summary>
public sealed class DhcpScopeRepositorySearchTests : IAsyncLifetime
{
    private readonly DhcpRepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five scopes with names deliberately out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by a real <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order happening to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder = ["delta", "bravo", "echo", "alpha", "charlie"];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new DhcpScopeEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    RangeStartAddress = "192.0.2.10",
                    RangeEndAddress = "192.0.2.200",
                    SubnetPrefixLength = 24,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryScopeOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        IReadOnlyList<DhcpScopeEntity> results = await repository.SearchAsync(skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha", "bravo", "charlie", "delta", "echo"],
            [.. results.Select(s => s.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        IReadOnlyList<DhcpScopeEntity> firstPage = await repository.SearchAsync(skip: 0, take: 2);
        IReadOnlyList<DhcpScopeEntity> secondPage = await repository.SearchAsync(skip: 2, take: 2);
        IReadOnlyList<DhcpScopeEntity> thirdPage = await repository.SearchAsync(skip: 4, take: 2);

        Assert.Equal(["alpha", "bravo"], [.. firstPage.Select(s => s.Name)]);
        Assert.Equal(["charlie", "delta"], [.. secondPage.Select(s => s.Name)]);
        Assert.Equal(["echo"], [.. thirdPage.Select(s => s.Name)]);
        Assert.DoesNotContain(secondPage, s => firstPage.Any(first => first.Id == s.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullScopeCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DhcpScopeEntity> onePage = await repository.SearchAsync(skip: 0, take: 2);

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        IReadOnlyList<DhcpScopeEntity> results = await repository.SearchAsync(skip: 100, take: 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsTheMatchingScope()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        DhcpScopeEntity found = await repository.FindByNameAsync("charlie");
        DhcpScopeEntity notFound = await repository.FindByNameAsync("does-not-exist");

        Assert.NotNull(found);
        Assert.Equal("charlie", found.Name);
        Assert.Null(notFound);
    }
}
