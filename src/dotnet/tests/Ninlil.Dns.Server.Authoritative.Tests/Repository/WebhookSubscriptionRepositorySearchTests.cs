using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.WebhookSubscriptionRepository" />'s
///     real, pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real
///     SQLite-backed database -- the Phase 13h proof that <c>WebhooksController</c>'s own
///     subscription listing no longer materializes every subscription into memory before slicing
///     a page out of it. This is a search of the subscription set itself, distinct from Phase
///     13e2's own separate, per-subscription delivery-attempt history listing, mirroring
///     <see cref="ZoneRepositorySearchTests" />'s own Phase 13g proof for the same shape.
/// </summary>
public sealed class WebhookSubscriptionRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five subscriptions with names deliberately out of alphabetical
    ///     creation order, so a passing ordering assertion can only be explained by a real
    ///     <c>ORDER BY</c> translated at the database, not by creation/insertion order happening
    ///     to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-subscription",
            "bravo-subscription",
            "echo-subscription",
            "alpha-subscription",
            "charlie-subscription",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new WebhookSubscriptionEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    TargetUrl = "https://example.com/webhook",
                    EventTypeNames = ["DnsZoneChanged"],
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEverySubscriptionOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        IReadOnlyList<WebhookSubscriptionEntity> results = await repository.SearchAsync(
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(
            [
                "alpha-subscription",
                "bravo-subscription",
                "charlie-subscription",
                "delta-subscription",
                "echo-subscription",
            ],
            [.. results.Select(subscription => subscription.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        IReadOnlyList<WebhookSubscriptionEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<WebhookSubscriptionEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(
            ["alpha-subscription", "bravo-subscription"],
            [.. firstPage.Select(s => s.Name)]
        );
        Assert.Equal(["echo-subscription"], [.. thirdPage.Select(s => s.Name)]);
        Assert.DoesNotContain(
            thirdPage,
            subscription => firstPage.Any(first => first.Id == subscription.Id)
        );
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullSubscriptionCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<WebhookSubscriptionEntity> onePage = await repository.SearchAsync(
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
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        IReadOnlyList<WebhookSubscriptionEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
