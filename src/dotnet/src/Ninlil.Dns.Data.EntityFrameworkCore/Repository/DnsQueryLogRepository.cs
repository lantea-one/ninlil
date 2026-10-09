using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsQueryLogRepository" /> against
///     <see cref="NinlilDnsContext" />. Every bulk delete uses <c>ExecuteDeleteAsync</c>, a single
///     <c>DELETE ... WHERE ...</c> statement, instead of loading rows into the change tracker,
///     which would be slow and use a lot of memory on a table of millions of rows. These deletes
///     therefore take effect immediately, not at <see cref="IUnitOfWork.SaveChangesAsync" /> like
///     other repository writes.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsQueryLogRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsQueryLogEntryEntity>(context),
        IDnsQueryLogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsQueryLogEntryEntity>> SearchAsync(
        DnsQueryLogSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await ApplyFilters(Set, criteria)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(
        DnsQueryLogSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) => await ApplyFilters(Set, criteria).CountAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(entry => entry.OccurredAtUtc < cutoffUtc).ExecuteDeleteAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> DeleteExcessAsync(
        int maxRetainedEntries,
        CancellationToken stoppingToken = default
    )
    {
        // A limit of zero or less means "no limit" and deletes nothing, as the cleanup service
        // treats it.
        if (maxRetainedEntries <= 0)
            return 0;

        // The newest maxRetainedEntries rows are the rows at or after the timestamp of the row at
        // position maxRetainedEntries (0-based) in newest-first order. Finding that timestamp is
        // a walk over the OccurredAtUtc index and loads one value, never a list of identifiers
        // (a NOT IN list of a million identifiers is slow or rejected on some engines).
        List<DateTimeOffset> cutoffs = await Set.OrderByDescending(entry => entry.OccurredAtUtc)
            .Skip(maxRetainedEntries)
            .Take(1)
            .Select(entry => entry.OccurredAtUtc)
            .ToListAsync(stoppingToken);

        // Fewer rows than the limit (or exactly the limit): nothing to remove.
        if (cutoffs.Count == 0)
            return 0;

        DateTimeOffset cutoff = cutoffs[0];

        // Every row older than the cutoff is beyond the limit.
        int deleted = await Set.Where(entry => entry.OccurredAtUtc < cutoff)
            .ExecuteDeleteAsync(stoppingToken);

        // Rows that share the cutoff timestamp are split by identifier, newest identifier first,
        // so exactly the limit remains. Only these tied rows are listed.
        int newerThanCutoff = await Set.CountAsync(
            entry => entry.OccurredAtUtc > cutoff,
            stoppingToken
        );
        int keepAtCutoff = maxRetainedEntries - newerThanCutoff;
        List<Guid> tiedIdsToDelete = await Set.Where(entry => entry.OccurredAtUtc == cutoff)
            .OrderByDescending(entry => entry.Id)
            .Skip(keepAtCutoff)
            .Select(entry => entry.Id)
            .ToListAsync(stoppingToken);

        // Delete in slices so a pathological number of identical timestamps stays below the
        // parameter limits of the engines.
        foreach (Guid[] slice in tiedIdsToDelete.Chunk(1000))
            deleted += await Set.Where(entry => slice.Contains(entry.Id))
                .ExecuteDeleteAsync(stoppingToken);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync(CancellationToken stoppingToken = default) =>
        await Set.ExecuteDeleteAsync(stoppingToken);

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken stoppingToken = default)
    {
        if (Context.Database.IsSqlite())
            await Context.Database.ExecuteSqlRawAsync("PRAGMA optimize;", stoppingToken);
    }

    /// <summary>
    ///     This method applies the filters in <paramref name="criteria" />. <see cref="SearchAsync" />
    ///     and <see cref="CountAsync" /> share it so they always agree on what matches. It is an
    ///     instance method because the domain filter checks
    ///     <see cref="EntityFrameworkRepositoryBase{TEntity}.Context" /> for the active provider.
    /// </summary>
    private IQueryable<DnsQueryLogEntryEntity> ApplyFilters(
        IQueryable<DnsQueryLogEntryEntity> query,
        DnsQueryLogSearchCriteria criteria
    )
    {
        if (criteria is null)
            return query;

        if (criteria.FromUtc is { } fromUtc)
            query = query.Where(entry => entry.OccurredAtUtc >= fromUtc);

        if (criteria.ToUtc is { } toUtc)
            query = query.Where(entry => entry.OccurredAtUtc <= toUtc);

        if (!string.IsNullOrWhiteSpace(criteria.ClientAddress))
            query = query.Where(entry => entry.ClientAddress == criteria.ClientAddress);

        if (!string.IsNullOrWhiteSpace(criteria.DomainContains))
        {
            string pattern = LikePatternEscaper.Contains(criteria.DomainContains);

            // LIKE is case-insensitive by default on SQLite, MySQL and SQL Server but
            // case-sensitive on PostgreSQL. ILike (ILIKE) makes PostgreSQL match the others and
            // the case-insensitive MongoDB search. It only translates on Npgsql, so it is used
            // only there. The term is matched literally: the pattern is escaped and the escape
            // character is passed to both functions.
            query = Context.Database.IsNpgsql()
                ? query.Where(entry =>
                    entry.QueriedName != null
                    && EF.Functions.ILike(
                        entry.QueriedName,
                        pattern,
                        LikePatternEscaper.EscapeCharacter
                    )
                )
                : query.Where(entry =>
                    entry.QueriedName != null
                    && EF.Functions.Like(
                        entry.QueriedName,
                        pattern,
                        LikePatternEscaper.EscapeCharacter
                    )
                );
        }

        if (criteria.QueriedType is { } queriedType)
            query = query.Where(entry => entry.QueriedType == queriedType);

        if (criteria.ResponseCode is { } responseCode)
            query = query.Where(entry => entry.ResponseCode == responseCode);

        if (!string.IsNullOrWhiteSpace(criteria.ClientIdentifier))
            query = query.Where(entry => entry.ClientIdentifier == criteria.ClientIdentifier);

        if (criteria.ClientProtocolVersion is { } clientProtocolVersion)
            query = query.Where(entry => entry.ClientProtocolVersion == clientProtocolVersion);

        return query;
    }
}
