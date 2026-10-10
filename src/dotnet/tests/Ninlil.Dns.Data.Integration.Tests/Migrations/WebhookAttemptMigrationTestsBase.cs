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
///     This class holds the tests of the <c>ConvertWebhookAttemptCreatedAtToTicks</c> migration
///     that run against each relational engine; one subclass per engine supplies the fixture.
///     Each test creates an empty database, applies the migrations up to the one before it,
///     inserts delivery attempts with raw SQL in the old column format, applies the remaining
///     migrations and reads the rows back through the repository.
/// </summary>
public abstract class WebhookAttemptMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "ConvertWebhookAttemptCreatedAtToTicks";

    private static readonly DateTimeOffset Created = new(2026, 10, 9, 8, 0, 0, 123, TimeSpan.Zero);

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    /// <summary>
    ///     This method returns the SQL literal for an instant in the column type the engine used
    ///     before the migration (text, timestamptz, datetime(6) or datetimeoffset).
    /// </summary>
    private string Literal(DateTimeOffset value) =>
        fixture.Engine switch
        {
            StorageEngineEnum.Sqlite => $"'{value:yyyy-MM-dd HH:mm:ss.fffffff}+00:00'",
            StorageEngineEnum.PostgreSql => $"'{value:yyyy-MM-dd HH:mm:ss.ffffff}+00'",
            StorageEngineEnum.MySql => $"'{value:yyyy-MM-dd HH:mm:ss.ffffff}'",
            _ => $"'{value:yyyy-MM-ddTHH:mm:ss.fffffff}+00:00'",
        };

    // Upper case: SQLite stores a Guid as upper-case text and compares it case-sensitively.
    private static string Id(Guid id) => $"'{id.ToString().ToUpperInvariant()}'";

    private async Task<(EngineDatabase Database, NinlilDnsContext Context)> MigrateToBeforeAsync()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        NinlilDnsContext context = database.CreateContext();

        List<string> migrations = [.. context.Database.GetMigrations()];
        int index = migrations.FindIndex(name => name.EndsWith("_" + MigrationName));
        Assert.True(index > 0, "The migration is missing from the provider's assembly.");

        await context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        return (database, context);
    }

    private async Task InsertAttemptAsync(
        NinlilDnsContext context,
        Guid subscriptionId,
        DateTimeOffset createdAt
    )
    {
        string columns = string.Join(
            ", ",
            new[]
            {
                "Id",
                "CreatedAtUtc",
                "SubscriptionId",
                "SubscriptionName",
                "EventTypeName",
                "PayloadJson",
                "Status",
                "AttemptCount",
                "MaxAttempts",
            }.Select(Quote)
        );

        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDnsWebhookDeliveryAttempts")} ({columns}) VALUES ("
                + $"{Id(Guid.NewGuid())}, {Literal(createdAt)}, {Id(subscriptionId)}, 'sub', "
                + "'ZoneCreated', '{{}}', 'Succeeded', 1, 3)"
        );
    }

    private static async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ReadAsync(
        EngineDatabase database,
        Guid subscriptionId
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>()
            .SearchBySubscriptionIdAsync(subscriptionId, 0, 100);
    }

    /// <summary>
    ///     Attempts inserted before the migration keep their creation instants and come back
    ///     newest first.
    /// </summary>
    [Fact]
    public async Task Migrate_ConvertsExistingAttemptTimesToTicks_PreservingTheInstants()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            Guid subscriptionId = Guid.NewGuid();
            await InsertAttemptAsync(context, subscriptionId, Created.AddMinutes(-5));
            await InsertAttemptAsync(context, subscriptionId, Created);
            await InsertAttemptAsync(context, subscriptionId, Created.AddMinutes(-10));

            await context.Database.MigrateAsync();

            IReadOnlyList<WebhookDeliveryAttemptEntity> attempts = await ReadAsync(
                database,
                subscriptionId
            );
            Assert.Equal(
                [Created, Created.AddMinutes(-5), Created.AddMinutes(-10)],
                attempts.Select(attempt => attempt.CreatedAtUtc)
            );
        }
    }

    /// <summary>
    ///     Migrating down converts the ticks back to the old column type and up again without
    ///     changing the instants.
    /// </summary>
    [Fact]
    public async Task Migrate_DownThenUp_KeepsTheInstants()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            string before = context.Database.GetAppliedMigrations().Last();
            Guid subscriptionId = Guid.NewGuid();
            await InsertAttemptAsync(context, subscriptionId, Created);

            await context.Database.MigrateAsync();
            await context.GetService<IMigrator>().MigrateAsync(before);

            // Applying the migration again converts the restored old-format values to ticks
            // once more, so reading them back shows Down kept the instants.
            await context.Database.MigrateAsync();

            IReadOnlyList<WebhookDeliveryAttemptEntity> attempts = await ReadAsync(
                database,
                subscriptionId
            );
            Assert.Equal(Created, Assert.Single(attempts).CreatedAtUtc);
        }
    }
}
