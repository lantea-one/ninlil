using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsAccessControlRuleRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>DnsAccessControlRulesController</c>'s listing needs, rather
///     than in memory.
/// </summary>
public sealed class DnsAccessControlRuleRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five rules with names out of alphabetical creation
    ///     order, so a passing ordering assertion can only be explained by an <c>ORDER BY</c>
    ///     translated at the database, not by creation/insertion order already matching.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-rule",
            "bravo-rule",
            "echo-rule",
            "alpha-rule",
            "charlie-rule",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new DnsAccessControlRuleEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    Cidr = "10.0.0.0/24",
                    Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryRuleOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        IReadOnlyList<DnsAccessControlRuleEntity> results = await repository.SearchAsync(
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-rule", "bravo-rule", "charlie-rule", "delta-rule", "echo-rule"],
            [.. results.Select(rule => rule.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        IReadOnlyList<DnsAccessControlRuleEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsAccessControlRuleEntity> secondPage = await repository.SearchAsync(
            skip: 2,
            take: 2
        );
        IReadOnlyList<DnsAccessControlRuleEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(["alpha-rule", "bravo-rule"], [.. firstPage.Select(r => r.Name)]);
        Assert.Equal(["charlie-rule", "delta-rule"], [.. secondPage.Select(r => r.Name)]);
        Assert.Equal(["echo-rule"], [.. thirdPage.Select(r => r.Name)]);

        Assert.DoesNotContain(secondPage, rule => firstPage.Any(first => first.Id == rule.Id));
        Assert.DoesNotContain(thirdPage, rule => secondPage.Any(second => second.Id == rule.Id));
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullRuleCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<DnsAccessControlRuleEntity> onePage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        IReadOnlyList<DnsAccessControlRuleEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
