using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsQueryLogRepository" /> against
///     <see cref="NinlilDnsContext" />, using Entity Framework Core's own
///     <c>ExecuteDeleteAsync</c> query extension for every bulk-delete method -- a genuine,
///     translated <c>DELETE ... WHERE ...</c>
///     statement, rather than loading each matching row into this repository's own change tracker
///     only to immediately discard it, which would be both slower and a needless memory spike for
///     a table that can hold millions of rows. Because of that, every delete here commits
///     immediately against the database rather than being staged for
///     <see cref="IUnitOfWork.SaveChangesAsync" /> the way this project's every other repository
///     write is -- there is nothing left to stage once the statement has already run.
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
        // Only the identifiers of the entries to keep are loaded into memory -- bounded by
        // maxRetainedEntries itself, never by the table's own total size -- then every entry not
        // in that set is removed in one statement. If the table currently holds no more than
        // maxRetainedEntries rows to begin with, every row's own identifier ends up in the "keep"
        // set, and the delete below correctly removes nothing.
        List<Guid> idsToKeep = await Set.OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(maxRetainedEntries)
            .Select(entry => entry.Id)
            .ToListAsync(stoppingToken);

        if (idsToKeep.Count == 0)
            return 0;

        return await Set.Where(entry => !idsToKeep.Contains(entry.Id))
            .ExecuteDeleteAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync(CancellationToken stoppingToken = default) =>
        await Set.ExecuteDeleteAsync(stoppingToken);

    /// <summary>
    ///     This method narrows a query by every filter <paramref name="criteria" /> specifies,
    ///     shared between <see cref="SearchAsync" /> and <see cref="CountAsync" /> so the two can
    ///     never drift out of sync with each other over what "matches" actually means. This is an
    ///     instance method, not a static one, specifically so the domain-substring matching below
    ///     can consult <see cref="EntityFrameworkRepositoryBase{TEntity}.Context" /> to find out
    ///     which relational provider is active.
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
            string pattern = $"%{criteria.DomainContains}%";

            // Unlike SQLite/MySQL/SqlServer, PostgreSQL's own EF.Functions.Like translation is
            // case-sensitive by default (it maps directly to PostgreSQL's own case-sensitive
            // LIKE operator), so identical search input would otherwise return a different,
            // narrower result set purely because PostgreSQL happens to be the active provider.
            // Npgsql's EF.Functions.ILike translates to PostgreSQL's own case-insensitive ILIKE
            // operator instead, matching every other provider's default LIKE behavior -- and the
            // MongoDB repository's own case-insensitive regex search. ILike is PostgreSQL-only
            // (it doesn't translate against any other provider's query pipeline), so it's used
            // only when Npgsql is actually the active provider; every other provider keeps using
            // plain EF.Functions.Like, which is already case-insensitive there.
            query = Context.Database.IsNpgsql()
                ? query.Where(entry =>
                    entry.QueriedName != null && EF.Functions.ILike(entry.QueriedName, pattern)
                )
                : query.Where(entry =>
                    entry.QueriedName != null && EF.Functions.Like(entry.QueriedName, pattern)
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
