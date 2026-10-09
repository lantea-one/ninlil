using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class holds the tests of the <c>QueryPerformanceIndexes</c> migration that run
///     against each relational engine; one subclass per engine supplies the fixture. The
///     migration only changes indexes, so the repositories work on the schema before and after
///     it. One test migrates to the step before it, writes query-log rows through the
///     repository, applies the migration (and goes down and up again) and checks that the rows
///     and the filtered searches survive. The other test applies all migrations to an empty
///     database, loads 20,000 rows and asserts, from the engine's own plan output, that a
///     client-address page reads the composite <c>(ClientAddress, OccurredAtUtc)</c> index.
///     SQL Server has no plan assertion (its plan is an XML document whose index choice depends
///     on statistics that the test cannot force), so there the test asserts that the new index
///     exists and the old one does not.
/// </summary>
public abstract class QueryPerformanceIndexesMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "QueryPerformanceIndexes";

    private const string Table = "NinlilDnsQueryLogEntries";

    private const string ClientAddressIndex = "IX_NinlilDnsQueryLogEntries_ClientAddress";

    private const string CompositeIndex = ClientAddressIndex + "_OccurredAtUtc";

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    private static DnsQueryLogEntryEntity NewEntry(int number, string clientAddress) =>
        new()
        {
            Id = Guid.CreateVersion7(BaseTime.AddSeconds(number)),
            CreatedAtUtc = BaseTime.AddSeconds(number),
            OccurredAtUtc = BaseTime.AddSeconds(number),
            ClientAddress = clientAddress,
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = $"host{number}.example",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode =
                number % 2 == 0 ? DnsResponseCodeEnum.NoError : DnsResponseCodeEnum.NxDomain,
            AnswerCount = 1,
            ClientIdentifier = $"client-{number % 5}",
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

    /// <summary>
    ///     This method runs <paramref name="sql" /> and returns every value of every row as one
    ///     text, which is enough to look for an index name in a plan.
    /// </summary>
    private static async Task<string> RunAsync(NinlilDnsContext context, string sql)
    {
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync();

        StringBuilder text = new();
        while (await reader.ReadAsync())
        {
            for (int column = 0; column < reader.FieldCount; column++)
                text.Append(reader.GetValue(column)).Append(' ');
            text.AppendLine();
        }
        return text.ToString();
    }

    /// <summary>
    ///     Query-log rows written before the migration survive it, the filtered searches return
    ///     them afterwards, and the migration can be reverted and applied again.
    /// </summary>
    [Fact]
    public async Task Migrate_WithQueryLogRowsPresent_KeepsTheRowsAndTheFilteredSearches()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        await using (database)
        {
            NinlilDnsContext context = database.CreateContext();
            List<string> migrations = [.. context.Database.GetMigrations()];
            int index = migrations.FindIndex(name => name.EndsWith("_" + MigrationName));
            Assert.True(index > 0, "The migration is missing from the provider's assembly.");

            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[index - 1]);

            await SeedAsync(
                database,
                Enumerable.Range(0, 30).Select(n => NewEntry(n, n < 10 ? "192.0.2.1" : "192.0.2.2"))
            );

            await context.Database.MigrateAsync();
            await migrator.MigrateAsync(migrations[index - 1]);
            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            IDnsQueryLogRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

            Assert.Equal(30, await repository.CountAsync(new DnsQueryLogSearchCriteria()));
            Assert.Equal(
                10,
                await repository.CountAsync(
                    new DnsQueryLogSearchCriteria(ClientAddress: "192.0.2.1")
                )
            );
            Assert.Equal(
                6,
                await repository.CountAsync(
                    new DnsQueryLogSearchCriteria(ClientIdentifier: "client-1")
                )
            );
            Assert.Equal(
                15,
                await repository.CountAsync(
                    new DnsQueryLogSearchCriteria(ResponseCode: DnsResponseCodeEnum.NxDomain)
                )
            );

            IReadOnlyList<DnsQueryLogEntryEntity> page = await repository.SearchAsync(
                new DnsQueryLogSearchCriteria(ClientAddress: "192.0.2.1"),
                0,
                3
            );
            Assert.Equal(
                ["host9.example", "host8.example", "host7.example"],
                page.Select(entry => entry.QueriedName).ToList()
            );
        }
    }

    /// <summary>
    ///     On a migrated database with 20,000 rows, the newest-first page of one client address
    ///     reads the composite index (SQLite, PostgreSQL, MySQL); on SQL Server the new index
    ///     exists and the single-column one is gone.
    /// </summary>
    [Fact]
    public async Task ClientAddressPage_OnAMigratedDatabase_UsesTheCompositeIndex()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        await using (database)
        {
            NinlilDnsContext context = database.CreateContext();
            await context.Database.MigrateAsync();

            await SeedAsync(
                database,
                Enumerable
                    .Range(0, 20_000)
                    .Select(n => NewEntry(n, $"10.0.{n % 200 / 100}.{n % 100}"))
            );

            string table = Quote(Table);
            string select =
                $"SELECT * FROM {table} WHERE {Quote("ClientAddress")} = '10.0.0.7' "
                + $"ORDER BY {Quote("OccurredAtUtc")} DESC";

            switch (fixture.Engine)
            {
                case StorageEngineEnum.Sqlite:
                    await RunAsync(context, "ANALYZE;");
                    Assert.Contains(
                        CompositeIndex,
                        await RunAsync(context, $"EXPLAIN QUERY PLAN {select} LIMIT 50")
                    );
                    break;
                case StorageEngineEnum.PostgreSql:
                    await RunAsync(context, $"ANALYZE {table}");
                    Assert.Contains(
                        CompositeIndex,
                        await RunAsync(context, $"EXPLAIN {select} LIMIT 50")
                    );
                    break;
                case StorageEngineEnum.MySql:
                    await RunAsync(context, $"ANALYZE TABLE {table}");
                    Assert.Contains(
                        CompositeIndex,
                        await RunAsync(context, $"EXPLAIN {select} LIMIT 50")
                    );
                    break;
                default:
                    string indexes = await RunAsync(
                        context,
                        "SELECT name FROM sys.indexes WHERE object_id = " + $"OBJECT_ID('{Table}')"
                    );
                    Assert.Contains(CompositeIndex, indexes);
                    Assert.DoesNotContain(ClientAddressIndex + " ", indexes);
                    break;
            }
        }
    }
}
