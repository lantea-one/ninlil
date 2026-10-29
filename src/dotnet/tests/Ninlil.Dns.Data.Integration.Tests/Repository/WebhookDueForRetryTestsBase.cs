using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of
///     <see cref="IWebhookDeliveryAttemptRepository.ListDueForRetryAsync" />, the bounded read of
///     the webhook retry sweep, that run against every engine: one subclass per engine supplies
///     the fixture and a fresh database.
/// </summary>
public abstract class WebhookDueForRetryTestsBase(EngineFixtureBase fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static WebhookDeliveryAttemptEntity Attempt(
        WebhookDeliveryAttemptStatusEnum status,
        DateTimeOffset? nextRetryDueUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = Start,
            SubscriptionId = Guid.NewGuid(),
            SubscriptionName = "sub",
            EventTypeName = "ZoneCreated",
            PayloadJson = "{}",
            Status = status,
            AttemptCount = 1,
            MaxAttempts = 5,
            NextRetryDueUtc = nextRetryDueUtc,
            CompletedAtUtc = status == WebhookDeliveryAttemptStatusEnum.Pending ? null : Start,
        };

    private static async Task SaveAsync(
        EngineDatabase database,
        IEnumerable<WebhookDeliveryAttemptEntity> attempts
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        foreach (WebhookDeliveryAttemptEntity attempt in attempts)
            await repository.AddAsync(attempt);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     The read returns only pending rows due at or before the moment asked for, the oldest
    ///     due first, never more than the limit, and never a terminal row, a row due later or a
    ///     pending row with no retry time. Two of the rows are due at the same moment, and which
    ///     of them comes first is not specified, so the test compares due times.
    /// </summary>
    [Fact]
    public async Task ListDueForRetryAsync_ReturnsAtMostMaxCountDueRowsOldestFirst()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DateTimeOffset asOf = Start.AddHours(1);

        // Six due rows inserted out of due-time order; two of them share a due time.
        DateTimeOffset[] dueTimes =
        [
            Start.AddMinutes(30),
            Start.AddMinutes(10),
            Start.AddMinutes(50),
            Start.AddMinutes(20),
            Start.AddMinutes(20),
            asOf,
        ];
        List<WebhookDeliveryAttemptEntity> due =
        [
            .. dueTimes.Select(time => Attempt(WebhookDeliveryAttemptStatusEnum.Pending, time)),
        ];
        List<WebhookDeliveryAttemptEntity> notDue =
        [
            Attempt(WebhookDeliveryAttemptStatusEnum.Pending, asOf.AddMinutes(1)),
            Attempt(WebhookDeliveryAttemptStatusEnum.Pending, null),
            Attempt(WebhookDeliveryAttemptStatusEnum.Succeeded, null),
            Attempt(WebhookDeliveryAttemptStatusEnum.DeadLettered, null),
        ];
        await SaveAsync(database, [.. due, .. notDue]);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository reader =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();

        IReadOnlyList<WebhookDeliveryAttemptEntity> all = await reader.ListDueForRetryAsync(
            asOf,
            100
        );
        Assert.Equal(
            due.Select(attempt => attempt.Id).Order(),
            all.Select(attempt => attempt.Id).Order()
        );
        Assert.Equal(dueTimes.Order(), all.Select(attempt => attempt.NextRetryDueUtc.Value));

        IReadOnlyList<WebhookDeliveryAttemptEntity> three = await reader.ListDueForRetryAsync(
            asOf,
            3
        );
        Assert.Equal(
            dueTimes.Order().Take(3),
            three.Select(attempt => attempt.NextRetryDueUtc.Value)
        );

        Assert.Empty(await reader.ListDueForRetryAsync(asOf, 0));
        Assert.Empty(await reader.ListDueForRetryAsync(Start, 100));
        Assert.Single(await reader.ListDueForRetryAsync(Start.AddMinutes(10), 100));
    }

    /// <summary>
    ///     Reading batches of 500 and moving each batch's rows out of the due state, as the retry
    ///     sweep does, returns every one of 1,203 due rows exactly once, each batch no larger
    ///     than 500, in due order, with the last batch short.
    /// </summary>
    [Fact]
    public async Task ListDueForRetryAsync_InBatchesOf500_ReturnsEveryDueRowOnce()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DateTimeOffset asOf = Start.AddDays(1);
        List<WebhookDeliveryAttemptEntity> rows =
        [
            .. Enumerable
                .Range(0, 1203)
                .Select(number =>
                    Attempt(
                        WebhookDeliveryAttemptStatusEnum.Pending,
                        Start.AddSeconds(number * 7 % 1203)
                    )
                ),
        ];
        await SaveAsync(database, rows);

        // The batch entities are changed below, so what the test compares is copied first.
        List<(Guid Id, DateTimeOffset DueUtc)> read = [];
        List<int> batchSizes = [];
        while (true)
        {
            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            IWebhookDeliveryAttemptRepository repository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();

            IReadOnlyList<WebhookDeliveryAttemptEntity> batch =
                await repository.ListDueForRetryAsync(asOf, 500);
            batchSizes.Add(batch.Count);
            read.AddRange(batch.Select(attempt => (attempt.Id, attempt.NextRetryDueUtc.Value)));

            foreach (WebhookDeliveryAttemptEntity attempt in batch)
            {
                attempt.Status = WebhookDeliveryAttemptStatusEnum.Succeeded;
                attempt.NextRetryDueUtc = null;
                attempt.CompletedAtUtc = asOf;
                repository.Update(attempt);
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            if (batch.Count < 500)
                break;
        }

        Assert.Equal([500, 500, 203], batchSizes);
        Assert.Equal(1203, read.Select(item => item.Id).Distinct().Count());
        Assert.Equal(
            rows.Select(attempt => attempt.NextRetryDueUtc.Value).Order(),
            read.Select(item => item.DueUtc)
        );
    }
}

/// <summary>This class runs <see cref="WebhookDueForRetryTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteWebhookDueForRetryTests(SqliteEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookDueForRetryTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlWebhookDueForRetryTests(PostgreSqlEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookDueForRetryTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlWebhookDueForRetryTests(MySqlEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);

/// <summary>This class runs <see cref="WebhookDueForRetryTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerWebhookDueForRetryTests(SqlServerEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookDueForRetryTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbWebhookDueForRetryTests(MongoDbEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookDueForRetryTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneWebhookDueForRetryTests(MongoDbStandaloneEngineFixture fixture)
    : WebhookDueForRetryTestsBase(fixture);
