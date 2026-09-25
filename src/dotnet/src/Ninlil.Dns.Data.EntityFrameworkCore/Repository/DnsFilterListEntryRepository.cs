using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsFilterListEntryRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsFilterListEntryRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsFilterListEntryEntity>(context),
        IDnsFilterListEntryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(entry => entry.ListId == listId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(entry => entry.ListId == listId)
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
        // A refresh swaps the whole set rather than diffing it: remove every current entry,
        // then add the new ones.
        List<DnsFilterListEntryEntity> existing = await Set.Where(entry => entry.ListId == listId)
            .ToListAsync(stoppingToken);
        Set.RemoveRange(existing);

        // Every new entry gets the same creation time, since they come from one refresh.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DnsFilterListReplacementEntry entry in entries)
            await Set.AddAsync(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ListId = listId,
                    Value = entry.Value,
                    IsException = entry.IsException,
                },
                stoppingToken
            );
    }
}
