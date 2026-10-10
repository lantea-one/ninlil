using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of the database-side delivery-attempt page
///     (<see cref="IWebhookDeliveryAttemptRepository.SearchBySubscriptionIdAsync" /> and
///     <see cref="IWebhookDeliveryAttemptRepository.CountBySubscriptionIdAsync" />) that run
///     against every engine: one subclass per engine supplies the fixture and a fresh database.
/// </summary>
public abstract class WebhookAttemptPagingTestsBase(EngineFixtureBase fixture)
{
    private const int Count = 25;

    private static readonly DateTimeOffset Start = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static WebhookDeliveryAttemptEntity Attempt(Guid subscriptionId, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = at,
            SubscriptionId = subscriptionId,
            SubscriptionName = "sub",
            EventTypeName = "ZoneCreated",
            PayloadJson = "{}",
            Status = WebhookDeliveryAttemptStatusEnum.Succeeded,
            AttemptCount = 1,
            MaxAttempts = 3,
        };

    /// <summary>
    ///     The pages come back newest first, are disjoint, cover every attempt of the
    ///     subscription once, and the count ignores other subscriptions.
    /// </summary>
    [Fact]
    public async Task SearchBySubscriptionIdAsync_PagesNewestFirst_AndCountMatches()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid subscriptionId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();

        // Two attempts share a timestamp so the Id tie-break is exercised.
        List<DateTimeOffset> times =
        [
            .. Enumerable.Range(0, Count - 1).Select(index => Start.AddSeconds(index * 7)),
            Start.AddSeconds(7),
        ];
        // Insert out of time order so the sort, not insertion order, decides.
        List<WebhookDeliveryAttemptEntity> mine =
        [
            .. times.OrderBy(_ => Guid.NewGuid()).Select(time => Attempt(subscriptionId, time)),
        ];

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IWebhookDeliveryAttemptRepository repository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            foreach (WebhookDeliveryAttemptEntity attempt in mine)
                await repository.AddAsync(attempt);
            await repository.AddAsync(Attempt(otherId, Start.AddDays(1)));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository reader =
            check.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();

        Assert.Equal(Count, await reader.CountBySubscriptionIdAsync(subscriptionId));
        Assert.Equal(1, await reader.CountBySubscriptionIdAsync(otherId));
        Assert.Equal(0, await reader.CountBySubscriptionIdAsync(Guid.NewGuid()));

        List<WebhookDeliveryAttemptEntity> all = [];
        for (int skip = 0; skip < Count; skip += 10)
        {
            IReadOnlyList<WebhookDeliveryAttemptEntity> page =
                await reader.SearchBySubscriptionIdAsync(subscriptionId, skip, 10);
            Assert.Equal(Math.Min(10, Count - skip), page.Count);
            all.AddRange(page);
        }

        Assert.Empty(await reader.SearchBySubscriptionIdAsync(subscriptionId, Count, 10));
        Assert.Equal(Count, all.Select(attempt => attempt.Id).Distinct().Count());
        Assert.All(all, attempt => Assert.Equal(subscriptionId, attempt.SubscriptionId));

        // CreatedAtUtc descending, times read back equal to the millisecond. The Id tie-break
        // is deterministic but follows each engine's own Guid ordering (SQL Server sorts
        // uniqueidentifier differently from .NET), so only the distinct-rows check above
        // covers the tied pair.
        Assert.Equal(
            mine.Select(attempt => attempt.CreatedAtUtc).Order().Reverse(),
            all.Select(attempt => attempt.CreatedAtUtc)
        );
    }
}

/// <summary>This class runs <see cref="WebhookAttemptPagingTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteWebhookAttemptPagingTests(SqliteEngineFixture fixture)
    : WebhookAttemptPagingTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookAttemptPagingTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlWebhookAttemptPagingTests(PostgreSqlEngineFixture fixture)
    : WebhookAttemptPagingTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookAttemptPagingTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlWebhookAttemptPagingTests(MySqlEngineFixture fixture)
    : WebhookAttemptPagingTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookAttemptPagingTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerWebhookAttemptPagingTests(SqlServerEngineFixture fixture)
    : WebhookAttemptPagingTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookAttemptPagingTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbWebhookAttemptPagingTests(MongoDbEngineFixture fixture)
    : WebhookAttemptPagingTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookAttemptPagingTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneWebhookAttemptPagingTests(
    MongoDbStandaloneEngineFixture fixture
) : WebhookAttemptPagingTestsBase(fixture);
