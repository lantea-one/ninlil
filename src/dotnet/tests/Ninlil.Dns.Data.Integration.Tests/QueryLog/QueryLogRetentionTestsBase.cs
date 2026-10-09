using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.QueryLog;

/// <summary>
///     This class holds the retention-by-count tests (<c>DeleteExcessAsync</c>) that run against
///     every engine's real server: one subclass per engine supplies the fixture. Each test seeds
///     a fresh per-test database. The tests cover the boundary rules (fewer rows than the limit,
///     exactly the limit), rows that share the cutoff timestamp (the greatest identifiers are
///     kept) and a table of 10,000 rows, where the earlier <c>NOT IN</c> implementation timed
///     out on SQL Server. The MongoDB-only large table test lives in
///     <see cref="MongoDbQueryLogRetentionLargeTests" />. The SQLite counterpart with the
///     cleanup service is in <c>Ninlil.Dns.Server.QueryLog.Tests</c>.
/// </summary>
public abstract class QueryLogRetentionTestsBase(EngineFixtureBase fixture)
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     This method builds an identifier whose order is the same under every engine's
    ///     comparison: its first and its last group both grow with <paramref name="number" />
    ///     (SQL Server compares the last bytes of a <c>uniqueidentifier</c> first, the others
    ///     the first bytes).
    /// </summary>
    private static Guid OrderedId(int number) =>
        Guid.Parse($"{number:x8}-0000-0000-0000-{number:x12}");

    private static DnsQueryLogEntryEntity NewEntry(int number, DateTimeOffset occurredAtUtc) =>
        new()
        {
            Id = OrderedId(number),
            CreatedAtUtc = occurredAtUtc,
            OccurredAtUtc = occurredAtUtc,
            ClientAddress = "203.0.113.10",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = $"host{number}.example",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };

    private static async Task SeedAsync(
        EngineDatabase database,
        IEnumerable<DnsQueryLogEntryEntity> entries
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .AddRangeAsync([.. entries]);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<int> DeleteExcessAsync(EngineDatabase database, int max)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .DeleteExcessAsync(max);
    }

    private static async Task<List<int>> RemainingNumbersAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IReadOnlyList<DnsQueryLogEntryEntity> all = await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .SearchAsync(new DnsQueryLogSearchCriteria(), 0, int.MaxValue);
        return [.. all.Select(entry => Convert.ToInt32(entry.QueriedName[4..^8])).OrderBy(n => n)];
    }

    /// <summary>
    ///     A thousand rows with distinct timestamps, keep 600: the 600 newest remain.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_ThousandRowsKeepSixHundred_KeepsTheNewestSixHundred()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await SeedAsync(
            database,
            Enumerable.Range(0, 1000).Select(n => NewEntry(n, BaseTime.AddSeconds(n)))
        );

        int deleted = await DeleteExcessAsync(database, 600);

        Assert.Equal(400, deleted);
        Assert.Equal(Enumerable.Range(400, 600).ToList(), await RemainingNumbersAsync(database));
    }

    /// <summary>
    ///     Ten rows share the cutoff timestamp and the limit splits them: exactly the limit
    ///     remains and the kept tied rows are those with the greatest identifiers.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_TenRowsShareTheCutoff_KeepsTheGreatestIdsAmongThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        // Numbers 0..9 are older; 10..19 share one timestamp; 20..29 are newer.
        List<DnsQueryLogEntryEntity> entries = [];
        for (int n = 0; n < 10; n++)
            entries.Add(NewEntry(n, BaseTime.AddMinutes(n)));
        for (int n = 10; n < 20; n++)
            entries.Add(NewEntry(n, BaseTime.AddHours(1)));
        for (int n = 20; n < 30; n++)
            entries.Add(NewEntry(n, BaseTime.AddHours(2).AddMinutes(n)));
        await SeedAsync(database, entries);

        // Keep 14: the ten newer rows plus the four tied rows with the greatest ids (16..19).
        int deleted = await DeleteExcessAsync(database, 14);

        Assert.Equal(16, deleted);
        Assert.Equal(Enumerable.Range(16, 14).ToList(), await RemainingNumbersAsync(database));
    }

    /// <summary>
    ///     When the limit lands exactly on the first tied row, no tied row is removed beyond
    ///     those the limit excludes.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_LimitCoversAllTiedRows_KeepsAllOfThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        List<DnsQueryLogEntryEntity> entries = [];
        for (int n = 0; n < 5; n++)
            entries.Add(NewEntry(n, BaseTime));
        for (int n = 5; n < 10; n++)
            entries.Add(NewEntry(n, BaseTime.AddMinutes(1)));
        await SeedAsync(database, entries);

        int deleted = await DeleteExcessAsync(database, 5);

        Assert.Equal(5, deleted);
        Assert.Equal(Enumerable.Range(5, 5).ToList(), await RemainingNumbersAsync(database));
    }

    /// <summary>
    ///     Fewer rows than the limit: nothing is deleted.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_FewerRowsThanTheLimit_DeletesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await SeedAsync(
            database,
            Enumerable.Range(0, 50).Select(n => NewEntry(n, BaseTime.AddSeconds(n)))
        );

        Assert.Equal(0, await DeleteExcessAsync(database, 100));
        Assert.Equal(50, (await RemainingNumbersAsync(database)).Count);
    }

    /// <summary>
    ///     A limit equal to the row count: nothing is deleted.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_LimitEqualToTheRowCount_DeletesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await SeedAsync(
            database,
            Enumerable.Range(0, 50).Select(n => NewEntry(n, BaseTime.AddSeconds(n)))
        );

        Assert.Equal(0, await DeleteExcessAsync(database, 50));
        Assert.Equal(50, (await RemainingNumbersAsync(database)).Count);
    }

    /// <summary>
    ///     An empty table or a limit of zero: nothing is deleted.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_EmptyTableOrZeroLimit_DeletesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        Assert.Equal(0, await DeleteExcessAsync(database, 10));

        await SeedAsync(database, [NewEntry(1, BaseTime)]);
        Assert.Equal(0, await DeleteExcessAsync(database, 0));
    }

    /// <summary>
    ///     Ten thousand rows, keep nine thousand: the SQL Server regression where the earlier
    ///     <c>NOT IN</c> delete passed the 30 second command timeout.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_TenThousandRows_FinishesAndKeepsTheNewest()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await SeedAsync(
            database,
            Enumerable.Range(0, 10_000).Select(n => NewEntry(n, BaseTime.AddSeconds(n)))
        );

        int deleted = await DeleteExcessAsync(database, 9_000);

        Assert.Equal(1_000, deleted);
        List<int> remaining = await RemainingNumbersAsync(database);
        Assert.Equal(9_000, remaining.Count);
        Assert.Equal(1_000, remaining[0]);
    }
}

/// <summary>This class runs <see cref="QueryLogRetentionTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteQueryLogRetentionTests(SqliteEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>This class runs <see cref="QueryLogRetentionTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlQueryLogRetentionTests(PostgreSqlEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>This class runs <see cref="QueryLogRetentionTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlQueryLogRetentionTests(MySqlEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>This class runs <see cref="QueryLogRetentionTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerQueryLogRetentionTests(SqlServerEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogRetentionTestsBase" /> on a MongoDB replica set
///     (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbQueryLogRetentionTests(MongoDbEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogRetentionTestsBase" /> on a standalone MongoDB
///     (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneQueryLogRetentionTests(MongoDbStandaloneEngineFixture fixture)
    : QueryLogRetentionTestsBase(fixture);

/// <summary>
///     This class holds the MongoDB regression test for the size cap on a large collection: the
///     earlier implementation sent a <c>$nin</c> list of the identifiers to keep, which passes
///     the 16 MB command limit above roughly 600,000 documents, so the cap stopped working. The
///     seed is 700,000 documents, so the test carries the <c>Slow</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Slow")]
public sealed class MongoDbQueryLogRetentionLargeTests(MongoDbEngineFixture fixture)
{
    /// <summary>
    ///     Seven hundred thousand documents, keep 650,000: the delete succeeds and removes
    ///     50,000.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_SevenHundredThousandDocuments_Succeeds()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int chunk = 0; chunk < 140; chunk++)
        {
            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            List<DnsQueryLogEntryEntity> entries = new(5_000);
            for (int index = 0; index < 5_000; index++)
            {
                DateTimeOffset at = baseTime.AddSeconds(chunk * 5_000 + index);
                entries.Add(
                    new DnsQueryLogEntryEntity
                    {
                        Id = Guid.CreateVersion7(at),
                        CreatedAtUtc = at,
                        OccurredAtUtc = at,
                        ClientAddress = "203.0.113.10",
                        Protocol = DnsTransportProtocolEnum.Udp,
                        QueriedName = "bulk.example",
                        QueriedType = DnsResourceRecordTypeEnum.A,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        AnswerCount = 1,
                    }
                );
            }

            await scope
                .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
                .AddRangeAsync(entries);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            check.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deleted = await repository.DeleteExcessAsync(650_000);

        Assert.Equal(50_000, deleted);
        Assert.Equal(650_000, await repository.CountAsync(new DnsQueryLogSearchCriteria()));
    }
}
