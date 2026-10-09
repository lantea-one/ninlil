using System.Text.RegularExpressions;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class stores migration snapshots on MySQL, which has no schemas below the database. A
///     snapshot is a set of tables named <c>nsnap_&lt;id&gt;__&lt;table&gt;</c> in the
///     application's own database: each base table is copied with <c>CREATE TABLE ... LIKE</c>
///     (no foreign keys) and <c>INSERT ... SELECT</c>, and the manifest is the one row of
///     <c>nsnap_&lt;id&gt;__manifest</c>. The prefix is 22 characters and the longest Ninlil table
///     name is 36, below MySQL's limit of 64. An account with all privileges on the database can
///     do all of this, so no extra privilege is needed.
/// </summary>
internal sealed partial class MySqlMigrationSnapshotStore(NinlilDnsContext context)
    : ServerMigrationSnapshotStore(context, StorageEngineEnum.MySql)
{
    private const string ManifestName = "manifest";

    [GeneratedRegex(@"^nsnap_(?<id>[0-9]{14})__(?<table>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TablePattern();

    private static string Prefix(string id) => $"nsnap_{id}__";

    // Every base table of the database, snapshot tables included, which the catalog's main-schema
    // listing leaves out.
    private async Task<List<string>> ListAllTablesAsync(CancellationToken stoppingToken) =>
        [
            .. (
                await Catalog.QueryAsync(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'",
                    stoppingToken
                )
            ).Select(row => row[0]),
        ];

    /// <inheritdoc />
    protected override string ShortTextType => "varchar(32)";

    /// <inheritdoc />
    protected override string LongTextType => "longtext";

    /// <inheritdoc />
    protected override Task CreateContainerAsync(string id, CancellationToken stoppingToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task CopyTableAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    )
    {
        string copy = SnapshotTableReference(id, table);
        await Catalog.ExecuteAsync(
            $"CREATE TABLE {copy} LIKE {Catalog.Quote(table)}",
            stoppingToken
        );
        await Catalog.ExecuteAsync(
            $"INSERT INTO {copy} SELECT * FROM {Catalog.Quote(table)}",
            stoppingToken
        );
    }

    /// <inheritdoc />
    protected override string SnapshotTableReference(string id, string table) =>
        Catalog.Quote(Prefix(id) + table);

    /// <inheritdoc />
    protected override async Task<List<string>> ListSnapshotIdsAsync(
        CancellationToken stoppingToken
    ) =>
        [
            .. (await ListAllTablesAsync(stoppingToken))
                .Select(name => TablePattern().Match(name))
                .Where(match => match.Success)
                .Select(match => match.Groups["id"].Value)
                .Distinct(),
        ];

    /// <inheritdoc />
    protected override async Task<List<string>> ListSnapshotTablesAsync(
        string id,
        CancellationToken stoppingToken
    ) =>
        [
            .. (await ListAllTablesAsync(stoppingToken))
                .Select(name => TablePattern().Match(name))
                .Where(match => match.Success && match.Groups["id"].Value == id)
                .Select(match => match.Groups["table"].Value)
                .Where(name => name != ManifestName),
        ];

    /// <inheritdoc />
    protected override Task<List<string>> ListSnapshotColumnsAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    ) => Catalog.ListColumnsAsync(null, Prefix(id) + table, stoppingToken);

    /// <inheritdoc />
    protected override string ManifestTableReference(string id) =>
        SnapshotTableReference(id, ManifestName);

    /// <inheritdoc />
    protected override async Task DropSnapshotAsync(string id, CancellationToken stoppingToken)
    {
        // The manifest goes first: a snapshot without a manifest is never offered.
        await Catalog.ExecuteAsync(
            $"DROP TABLE IF EXISTS {ManifestTableReference(id)}",
            stoppingToken
        );
        foreach (string table in await ListSnapshotTablesAsync(id, stoppingToken))
            await Catalog.ExecuteAsync(
                $"DROP TABLE {SnapshotTableReference(id, table)}",
                stoppingToken
            );
    }

    /// <inheritdoc />
    protected override string DescribeLocation(string id) =>
        $"tables {Prefix(id)}* in database {Catalog.DatabaseName}";
}
