using System.Text.RegularExpressions;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class stores migration snapshots on SQL Server. A snapshot is a schema called
///     <c>ninlil_snapshot_&lt;id&gt;</c> in the application's own database. Each base table of the
///     default schema is copied with <c>SELECT * INTO</c>, and the manifest is the one row of
///     <c>__NinlilSnapshot</c> in that schema. Deleting a snapshot drops its tables, then the
///     schema. A <c>db_owner</c> user can do all of this, so no extra privilege is needed.
/// </summary>
internal sealed partial class SqlServerMigrationSnapshotStore(NinlilDnsContext context)
    : ServerMigrationSnapshotStore(context, StorageEngineEnum.SqlServer)
{
    private const string ManifestTable = "__NinlilSnapshot";

    [GeneratedRegex(@"^ninlil_snapshot_(?<id>[0-9]{14})$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();

    private static string SchemaName(string id) => "ninlil_snapshot_" + id;

    /// <inheritdoc />
    protected override string ShortTextType => "nvarchar(32)";

    /// <inheritdoc />
    protected override string LongTextType => "nvarchar(max)";

    /// <inheritdoc />
    protected override Task CreateContainerAsync(string id, CancellationToken stoppingToken) =>
        Catalog.ExecuteAsync($"CREATE SCHEMA {Catalog.Quote(SchemaName(id))}", stoppingToken);

    /// <inheritdoc />
    protected override Task CopyTableAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    ) =>
        Catalog.ExecuteAsync(
            $"SELECT * INTO {SnapshotTableReference(id, table)} FROM {Catalog.Quote(table)}",
            stoppingToken
        );

    /// <inheritdoc />
    protected override string SnapshotTableReference(string id, string table) =>
        $"{Catalog.Quote(SchemaName(id))}.{Catalog.Quote(table)}";

    /// <inheritdoc />
    protected override async Task<List<string>> ListSnapshotIdsAsync(
        CancellationToken stoppingToken
    ) =>
        [
            .. (await Catalog.QueryAsync("SELECT name FROM sys.schemas", stoppingToken))
                .Select(row => SchemaPattern().Match(row[0]))
                .Where(match => match.Success)
                .Select(match => match.Groups["id"].Value),
        ];

    /// <inheritdoc />
    protected override async Task<List<string>> ListSnapshotTablesAsync(
        string id,
        CancellationToken stoppingToken
    ) =>
        [
            .. (await Catalog.ListTablesAsync(SchemaName(id), stoppingToken)).Where(name =>
                name != ManifestTable
            ),
        ];

    /// <inheritdoc />
    protected override Task<List<string>> ListSnapshotColumnsAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    ) => Catalog.ListColumnsAsync(SchemaName(id), table, stoppingToken);

    /// <inheritdoc />
    protected override string ManifestTableReference(string id) =>
        SnapshotTableReference(id, ManifestTable);

    /// <inheritdoc />
    protected override async Task DropSnapshotAsync(string id, CancellationToken stoppingToken)
    {
        string schema = SchemaName(id);
        foreach (string table in await Catalog.ListTablesAsync(schema, stoppingToken))
            await Catalog.ExecuteAsync(
                $"DROP TABLE {Catalog.Quote(schema)}.{Catalog.Quote(table)}",
                stoppingToken
            );

        // The schema may not exist when a failed creation calls this.
        await Catalog.ExecuteAsync(
            $"IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{schema}') DROP SCHEMA {Catalog.Quote(schema)}",
            stoppingToken
        );
    }

    /// <inheritdoc />
    protected override string DescribeLocation(string id) =>
        $"schema {SchemaName(id)} in database {Catalog.DatabaseName}";
}
