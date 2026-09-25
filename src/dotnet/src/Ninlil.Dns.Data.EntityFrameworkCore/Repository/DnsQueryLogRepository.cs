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
        // Load only the identifiers to keep (at most maxRetainedEntries, whatever the table
        // size), then delete everything else in one statement. If the table has no more rows
        // than that, every row is kept and nothing is deleted.
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
            string pattern = $"%{criteria.DomainContains}%";

            // LIKE is case-insensitive by default on SQLite, MySQL and SQL Server but
            // case-sensitive on PostgreSQL. ILike (ILIKE) makes PostgreSQL match the others and
            // the case-insensitive MongoDB search. It only translates on Npgsql, so it is used
            // only there.
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
