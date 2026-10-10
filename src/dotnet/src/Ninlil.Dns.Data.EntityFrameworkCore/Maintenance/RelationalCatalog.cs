using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class reads and changes the catalog of the database behind a
///     <see cref="NinlilDnsContext" /> on PostgreSQL, MySQL and SQL Server: tables, columns and
///     foreign keys, with identifier quoting per provider. The server snapshot stores use it
///     because a snapshot must follow the schema as it is in the database, which can be older
///     than the entity model. It runs plain ADO.NET commands on the context's connection and
///     opens that connection only for the length of each call.
/// </summary>
internal sealed partial class RelationalCatalog(NinlilDnsContext context, StorageEngineEnum engine)
{
    /// <summary>
    ///     This property contains the table the EF Core migrator keeps its history in.
    /// </summary>
    public const string HistoryTable = "__EFMigrationsHistory";

    /// <summary>
    ///     This method matches the name of a table that belongs to a MySQL snapshot, so the
    ///     main-schema listing can leave it out.
    /// </summary>
    [GeneratedRegex(@"^nsnap_[0-9]{14}__", RegexOptions.CultureInvariant)]
    private static partial Regex MySqlSnapshotTablePattern();

    /// <summary>
    ///     This property contains the name of the database the context is connected to.
    /// </summary>
    public string DatabaseName => context.Database.GetDbConnection().Database;

    /// <summary>
    ///     This method quotes an identifier for the engine, doubling the closing quote character.
    /// </summary>
    public string Quote(string identifier) =>
        engine switch
        {
            StorageEngineEnum.MySql => "`" + identifier.Replace("`", "``") + "`",
            StorageEngineEnum.SqlServer => "[" + identifier.Replace("]", "]]") + "]",
            _ => "\"" + identifier.Replace("\"", "\"\"") + "\"",
        };

    // The expression that names the schema the context's tables live in. MySQL has no schemas
    // below the database, so its main "schema" is the database.
    private string MainSchemaExpression =>
        engine switch
        {
            StorageEngineEnum.PostgreSql => "current_schema()",
            StorageEngineEnum.SqlServer => "SCHEMA_NAME()",
            _ => "DATABASE()",
        };

    /// <summary>
    ///     This method runs a statement and returns the number of rows it affected.
    /// </summary>
    /// <param name="sql">The statement; parameters are named <c>@name</c>.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <param name="parameters">The parameter names (without <c>@</c>) and values.</param>
    public async Task<int> ExecuteAsync(
        string sql,
        CancellationToken stoppingToken,
        params (string Name, object Value)[] parameters
    )
    {
        // A catalog statement changes the database; it must never run on a replica connection.
        context.EnsureWritable("A snapshot or catalog statement");
        DbConnection connection = context.Database.GetDbConnection();
        bool opened = await OpenAsync(connection, stoppingToken);
        try
        {
            await using DbCommand command = Create(connection, sql, parameters);
            return await command.ExecuteNonQueryAsync(stoppingToken);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    ///     This method runs a query and returns every row as an array of the column values
    ///     converted to strings (null stays null).
    /// </summary>
    /// <param name="sql">The query; parameters are named <c>@name</c>.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <param name="parameters">The parameter names (without <c>@</c>) and values.</param>
    public async Task<List<string[]>> QueryAsync(
        string sql,
        CancellationToken stoppingToken,
        params (string Name, object Value)[] parameters
    )
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool opened = await OpenAsync(connection, stoppingToken);
        try
        {
            await using DbCommand command = Create(connection, sql, parameters);
            await using DbDataReader reader = await command.ExecuteReaderAsync(stoppingToken);
            List<string[]> rows = [];
            while (await reader.ReadAsync(stoppingToken))
            {
                string[] row = new string[reader.FieldCount];
                for (int index = 0; index < row.Length; index++)
                    row[index] = reader.IsDBNull(index)
                        ? null
                        : Convert.ToString(
                            reader.GetValue(index),
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                rows.Add(row);
            }

            return rows;
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> OpenAsync(
        DbConnection connection,
        CancellationToken stoppingToken
    )
    {
        if (connection.State == ConnectionState.Open)
            return false;

        await connection.OpenAsync(stoppingToken);
        return true;
    }

    private static DbCommand Create(
        DbConnection connection,
        string sql,
        (string Name, object Value)[] parameters
    )
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>
    ///     This method returns the base tables of a schema, sorted by name. With a null
    ///     <paramref name="schema" /> it lists the main schema and leaves out MySQL snapshot
    ///     tables and the migrator's lock table.
    /// </summary>
    public async Task<List<string>> ListTablesAsync(string schema, CancellationToken stoppingToken)
    {
        string where = schema == null ? MainSchemaExpression : "@schema";
        (string, object)[] parameters = schema == null ? [] : [("schema", schema)];
        List<string[]> rows = await QueryAsync(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = "
                + where
                + " AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
            stoppingToken,
            parameters
        );
        IEnumerable<string> names = rows.Select(row => row[0]);
        if (schema == null)
            names = names.Where(name =>
                !name.StartsWith("__EFMigrationsLock", StringComparison.Ordinal)
                && !MySqlSnapshotTablePattern().IsMatch(name)
            );
        return [.. names];
    }

    /// <summary>
    ///     This method returns the column names of a table in creation order. With a null
    ///     <paramref name="schema" /> the table is in the main schema.
    /// </summary>
    public async Task<List<string>> ListColumnsAsync(
        string schema,
        string table,
        CancellationToken stoppingToken
    )
    {
        string where = schema == null ? MainSchemaExpression : "@schema";
        (string, object)[] parameters =
            schema == null ? [("table", table)] : [("schema", schema), ("table", table)];
        List<string[]> rows = await QueryAsync(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = "
                + where
                + " AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            stoppingToken,
            parameters
        );
        return [.. rows.Select(row => row[0])];
    }

    /// <summary>
    ///     This method returns the foreign keys of the main schema as (child table, referenced
    ///     table, constraint name). It reads the engine's own catalog because
    ///     <c>INFORMATION_SCHEMA</c> cannot join a foreign key to the key it references on every
    ///     engine.
    /// </summary>
    public async Task<List<(string Table, string Referenced, string Name)>> ListForeignKeysAsync(
        CancellationToken stoppingToken
    )
    {
        string sql = engine switch
        {
            StorageEngineEnum.PostgreSql =>
                "SELECT child.relname, parent.relname, con.conname FROM pg_constraint con "
                    + "JOIN pg_class child ON child.oid = con.conrelid "
                    + "JOIN pg_class parent ON parent.oid = con.confrelid "
                    + "JOIN pg_namespace ns ON ns.oid = child.relnamespace "
                    + "WHERE con.contype = 'f' AND ns.nspname = current_schema()",
            StorageEngineEnum.SqlServer =>
                "SELECT OBJECT_NAME(fk.parent_object_id), OBJECT_NAME(fk.referenced_object_id), fk.name "
                    + "FROM sys.foreign_keys fk WHERE SCHEMA_NAME(fk.schema_id) = SCHEMA_NAME()",
            _ => "SELECT TABLE_NAME, REFERENCED_TABLE_NAME, CONSTRAINT_NAME "
                + "FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE()",
        };
        return [.. (await QueryAsync(sql, stoppingToken)).Select(row => (row[0], row[1], row[2]))];
    }

    /// <summary>
    ///     This method drops one foreign key of a table.
    /// </summary>
    public Task DropForeignKeyAsync(string table, string name, CancellationToken stoppingToken) =>
        ExecuteAsync(
            engine == StorageEngineEnum.MySql
                ? $"ALTER TABLE {Quote(table)} DROP FOREIGN KEY {Quote(name)}"
                : $"ALTER TABLE {Quote(table)} DROP CONSTRAINT {Quote(name)}",
            stoppingToken
        );

    /// <summary>
    ///     This method returns <paramref name="tables" /> ordered so that every table comes after
    ///     the tables it references. Self-references and references to tables outside the set are
    ///     ignored. It throws when the foreign keys form a cycle.
    /// </summary>
    public static List<string> OrderByForeignKeys(
        IEnumerable<string> tables,
        IEnumerable<(string Table, string Referenced, string Name)> foreignKeys
    )
    {
        List<string> ordered = [];
        HashSet<string> remaining = [.. tables];
        List<(string Table, string Referenced, string Name)> edges =
        [
            .. foreignKeys.Where(key =>
                key.Table != key.Referenced
                && remaining.Contains(key.Table)
                && remaining.Contains(key.Referenced)
            ),
        ];
        while (remaining.Count > 0)
        {
            List<string> ready =
            [
                .. remaining
                    .Where(table =>
                        !edges.Any(edge =>
                            edge.Table == table && remaining.Contains(edge.Referenced)
                        )
                    )
                    .OrderBy(table => table, StringComparer.Ordinal),
            ];
            if (ready.Count == 0)
                throw new InvalidOperationException(
                    "The foreign keys of the tables to restore form a cycle."
                );
            ordered.AddRange(ready);
            foreach (string table in ready)
                remaining.Remove(table);
        }

        return ordered;
    }
}
