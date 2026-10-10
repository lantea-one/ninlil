using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientEntity>(context),
        IDnsClientRepository
{
    /// <inheritdoc />
    public Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    ) =>
        (
            from address in Context.Set<DnsClientAddressEntity>()
            join client in Set on address.ClientId equals client.Id
            where
                address.ClientIdentifier == clientIdentifier
                && address.ProtocolVersion == protocolVersion
            select client
        ).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientEntity>> SearchAsync(
        DnsClientSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        criteria ??= DnsClientSearchCriteria.All;
        IQueryable<DnsClientEntity> filtered = Filter(criteria).AsNoTracking();
        IQueryable<DnsClientAddressEntity> addresses = Context.Set<DnsClientAddressEntity>();

        // Each ordering puts rows with a missing value last through an explicit leading key,
        // because engines sort NULL differently (PostgreSQL puts NULL first in a descending
        // order, SQLite, MySQL and SQL Server last).
        IOrderedQueryable<DnsClientEntity> ordered = criteria.SortOrder switch
        {
            DnsClientSortOrderEnum.LastSeen => filtered
                .OrderBy(client =>
                    addresses.Any(address =>
                        address.ClientId == client.Id && address.LastSeenUtc != null
                    )
                        ? 0
                        : 1
                )
                .ThenByDescending(client =>
                    addresses
                        .Where(address => address.ClientId == client.Id)
                        .Max(address => address.LastSeenUtc)
                ),
            DnsClientSortOrderEnum.DisplayName => filtered
                .OrderBy(client => client.DisplayName == null || client.DisplayName == "" ? 1 : 0)
                // Coalesced so a null and an empty name tie (leaving creation order to decide
                // between them), and lower-cased so the ordering is case-insensitive on every
                // provider regardless of its default collation.
                .ThenBy(client => (client.DisplayName ?? "").ToLower()),
            _ => filtered.OrderBy(client => client.CreatedAtUtc),
        };

        return await ordered
            .ThenBy(client => client.CreatedAtUtc)
            .ThenBy(client => client.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) => await Filter(criteria ?? DnsClientSearchCriteria.All).CountAsync(stoppingToken);

    /// <summary>
    ///     This method applies the filters shared by <see cref="SearchAsync" /> and
    ///     <see cref="CountAsync" /> as <c>WHERE</c> clauses; a presence filter becomes a correlated
    ///     <c>EXISTS</c> over the client's address rows.
    /// </summary>
    private IQueryable<DnsClientEntity> Filter(DnsClientSearchCriteria criteria)
    {
        IQueryable<DnsClientEntity> query = Set;
        IQueryable<DnsClientAddressEntity> addresses = Context.Set<DnsClientAddressEntity>();

        if (criteria.IsFavorite is { } favorite)
            query = query.Where(client => client.IsFavorite == favorite);

        if (criteria.SeenSinceUtc is { } seenSinceUtc)
            query = query.Where(client =>
                addresses.Any(address =>
                    address.ClientId == client.Id && address.LastSeenUtc >= seenSinceUtc
                )
            );

        if (criteria.NotSeenSinceUtc is { } notSeenSinceUtc)
            query = query.Where(client =>
                !addresses.Any(address =>
                    address.ClientId == client.Id && address.LastSeenUtc >= notSeenSinceUtc
                )
            );

        if (criteria.CreatedSinceUtc is { } createdSinceUtc)
            query = query.Where(client => client.CreatedAtUtc >= createdSinceUtc);

        return query;
    }
}
