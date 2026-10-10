using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsFilterListEntryRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
/// <param name="changeInterceptor">
///     The interceptor that raises the change signal. <see cref="ReplaceEntriesAsync" /> writes
///     with raw statements that <c>SaveChanges</c> never sees, so it reports its own change
///     through it. <see langword="null" /> (a repository built by hand) raises no signal.
/// </param>
public class DnsFilterListEntryRepository(
    NinlilDnsContext context,
    EntityChangeSaveChangesInterceptor changeInterceptor = null
) : EntityFrameworkRepositoryBase<DnsFilterListEntryEntity>(context), IDnsFilterListEntryRepository
{
    /// <summary>
    ///     This constant is the most parameters one insert statement carries. It is far below the
    ///     lowest provider limit (SQL Server: 2,100 parameters, 1,000 rows per <c>VALUES</c>)
    ///     because Microsoft.Data.Sqlite looks parameters up by name, so the cost of a statement
    ///     grows with the square of its parameter count: 2,000 parameters per statement took
    ///     three times as long on SQLite as 200 in a 100,000-row insert, with no gain elsewhere.
    /// </summary>
    private const int MaxParametersPerStatement = 200;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) =>
        // Read-only: the filter cache builds its index from these rows and nothing saves them
        // back, so tracking a list of 100,000 entries would only cost memory.
        await Set.AsNoTracking().Where(entry => entry.ListId == listId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(entry => entry.ListId == listId)
            .OrderBy(entry => entry.Value)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(entry => entry.ListId == listId, stoppingToken);

    /// <inheritdoc />
    public async Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> entries,
        CancellationToken stoppingToken = default
    )
    {
        // Both halves write: the raw insert below runs on the context's own connection, which the
        // read-only guard does not see, so a replica context is refused here.
        Context.EnsureWritable("The filter list entry replace");

        // A refresh swaps the whole set rather than diffing it. Both halves bypass the change
        // tracker: a subscribed list can hold hundreds of thousands of entries, and tracking them
        // and building one insert command per entity through SaveChanges costs about 12 KB of
        // allocation per entry. Neither half waits for SaveChanges, so the caller must hold a
        // transaction (see IDnsFilterListEntryRepository.ReplaceEntriesAsync).
        await Set.Where(entry => entry.ListId == listId).ExecuteDeleteAsync(stoppingToken);

        // Every new entry gets the same creation time, since they come from one refresh.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DnsFilterListEntryEntity> added = new(entries.Count);

        foreach (DnsFilterListReplacementEntry entry in entries)
            added.Add(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ListId = listId,
                    Value = entry.Value,
                    IsException = entry.IsException,
                }
            );

        await InsertRowsAsync(added, stoppingToken);

        // Raw statements bypass SaveChanges, so report the change here. Inside the caller's
        // transaction the signal is held until the commit.
        changeInterceptor?.RecordBulkChange(Context, typeof(DnsFilterListEntryEntity));
    }

    /// <summary>
    ///     This method inserts the rows with multi-row <c>INSERT ... VALUES</c> statements built
    ///     from the EF model, so the column names, the store types and the value converters (the
    ///     time ticks, the <c>Kind</c> text) are the ones the model declares and the SQL works on
    ///     every relational provider. The statements run on the context's connection and current
    ///     transaction. Rows per statement are bounded by <see cref="MaxParametersPerStatement" />.
    /// </summary>
    private async Task InsertRowsAsync(
        List<DnsFilterListEntryEntity> rows,
        CancellationToken stoppingToken
    )
    {
        if (rows.Count == 0)
            return;

        IEntityType entityType = Context.Model.FindEntityType(typeof(DnsFilterListEntryEntity));
        StoreObjectIdentifier table = StoreObjectIdentifier
            .Create(entityType, StoreObjectType.Table)!
            .Value;
        IProperty[] properties =
        [
            .. entityType.GetProperties().Where(property => property.GetColumnName(table) != null),
        ];
        ISqlGenerationHelper sql = (
            (IInfrastructure<IServiceProvider>)Context
        ).GetService<ISqlGenerationHelper>();

        string columns = string.Join(
            ", ",
            properties.Select(property => sql.DelimitIdentifier(property.GetColumnName(table)))
        );
        string tableName = sql.DelimitIdentifier(table.Name, table.Schema);
        int rowsPerStatement = Math.Max(1, MaxParametersPerStatement / properties.Length);

        DbConnection connection = Context.Database.GetDbConnection();
        bool opened = connection.State != ConnectionState.Open;

        if (opened)
            await connection.OpenAsync(stoppingToken);

        try
        {
            for (int offset = 0; offset < rows.Count; offset += rowsPerStatement)
            {
                int count = Math.Min(rowsPerStatement, rows.Count - offset);
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = Context.Database.CurrentTransaction?.GetDbTransaction();
                StringBuilder text = new();
                text.Append("INSERT INTO ")
                    .Append(tableName)
                    .Append(" (")
                    .Append(columns)
                    .Append(") VALUES ");

                for (int row = 0; row < count; row++)
                {
                    text.Append(row == 0 ? "(" : ", (");

                    for (int column = 0; column < properties.Length; column++)
                    {
                        IProperty property = properties[column];
                        string name = sql.GenerateParameterName($"p{row}_{column}");
                        command.Parameters.Add(
                            property
                                .GetRelationalTypeMapping()
                                .CreateParameter(
                                    command,
                                    name,
                                    property.GetGetter().GetClrValue(rows[offset + row]),
                                    property.IsNullable
                                )
                        );
                        text.Append(column == 0 ? "" : ", ").Append(name);
                    }

                    text.Append(')');
                }

                command.CommandText = text.ToString();
                await command.ExecuteNonQueryAsync(stoppingToken);
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
